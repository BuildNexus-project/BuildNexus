#!/usr/bin/env python3
"""Create real API-backed data before the measured JMeter workloads start.

Only Python's standard library is required. Each CSV row belongs to a different
Client, with an approved design, 50% construction progress and a partially paid
invoice. Setup requests and Kafka waits never enter JMeter's latency results.
"""

import argparse
import csv
import io
import json
import math
import os
from pathlib import Path
import secrets
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from dataclasses import dataclass


CSV_COLUMNS = (
    "email", "password", "client_id", "project_id", "invoice_id", "payment_id",
    "architect_id", "milestone_id",
)


class SetupError(Exception):
    """A useful setup error whose message contains no credentials or tokens."""


class ApiError(SetupError):
    def __init__(self, method, path, status, title=""):
        super().__init__(f"{method} {path} returned unexpected HTTP {status}.")
        self.status = status
        self.title = title


class NoRedirects(urllib.request.HTTPRedirectHandler):
    # Do not forward an authenticated setup request to a redirected host.
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class Api:
    def __init__(self, base_url, timeout):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout
        self.opener = urllib.request.build_opener(NoRedirects())

    def request(self, method, path, token=None, payload=None, *, expected=200,
                body=None, content_type="application/json", timeout=None):
        if payload is not None:
            body = json.dumps(payload).encode("utf-8")
        headers = {"Accept": "application/json"}
        if token:
            headers["Authorization"] = "Bearer " + token
        if body is not None:
            headers["Content-Type"] = content_type
        request = urllib.request.Request(
            self.base_url + path, data=body, headers=headers, method=method,
        )
        try:
            with self.opener.open(request, timeout=timeout or self.timeout) as response:
                if response.status != expected:
                    raise ApiError(method, path, response.status)
                raw = response.read()
        except urllib.error.HTTPError as error:
            # Keep only the title needed to recognise an eventual-consistency
            # response. Never print an API response body (it might echo input).
            try:
                problem = json.loads(error.read())
                title = problem.get("title", "") if isinstance(problem, dict) else ""
            except (ValueError, UnicodeError):
                title = ""
            raise ApiError(method, path, error.code, title) from None
        except (urllib.error.URLError, OSError, TimeoutError):
            raise SetupError(f"Could not reach the gateway during {method} {path}.") from None
        try:
            return json.loads(raw)
        except (ValueError, UnicodeError):
            raise SetupError(f"{method} {path} did not return valid JSON.") from None


def require(condition, message):
    if not condition:
        raise SetupError(message)


def record(value, context):
    require(isinstance(value, dict), f"{context}: expected a JSON object.")
    return value


def identifier(value, context):
    require(isinstance(value, str), f"{context}: missing identifier.")
    try:
        parsed = uuid.UUID(value)
    except ValueError:
        raise SetupError(f"{context}: invalid identifier.") from None
    require(parsed.int != 0, f"{context}: empty identifier.")
    return str(parsed)


@dataclass(repr=False)
class Account:
    email: str
    password: str
    user_id: str
    full_name: str
    token: str = ""


def login(api, email, password, role):
    response = record(api.request("POST", "/api/auth/login", payload={
        "email": email, "password": password,
    }), "Login")
    user = record(response.get("user"), "Login user")
    require(user.get("role") == role, f"Login did not produce the expected {role} role.")
    require(user.get("email", "").lower() == email.lower(), "Login returned another user.")
    token = response.get("accessToken")
    require(isinstance(token, str) and bool(token.strip()), "Login returned no access token.")
    return token, identifier(user.get("id"), "Login user")


def register(api, run_id, label):
    email = f"perf.{label}.{run_id}@loadtest.invalid"
    password = "Perf1!" + secrets.token_hex(12)
    full_name = f"Performance {label} {run_id}"
    response = record(api.request("POST", "/api/auth/register", payload={
        "fullName": full_name, "email": email, "password": password,
        "phoneNumber": "0771234567",
    }, expected=201), "Registration")
    require(response.get("role") == "Client", "New registration was not a Client.")
    require(response.get("email") == email, "Registration returned another user.")
    return Account(email, password, identifier(response.get("id"), "Registration"), full_name)


def staff_account(api, admin_token, run_id, label, role):
    account = register(api, run_id, label)
    response = record(api.request("PUT", f"/api/users/{account.user_id}", admin_token, {
        "fullName": account.full_name, "email": account.email, "role": role,
    }), "Role assignment")
    require(response.get("id") == account.user_id and response.get("role") == role,
            "Admin role assignment did not persist the expected role.")
    account.token, logged_in_id = login(api, account.email, account.password, role)
    require(logged_in_id == account.user_id, "Staff login returned another user.")
    return account


def wait_for_read(api, path, token, poll_timeout, pending_responses):
    """Retry only the explicitly documented ownership/plan propagation delays."""
    deadline = time.monotonic() + poll_timeout
    while True:
        remaining = deadline - time.monotonic()
        require(remaining > 0, f"Timed out waiting for Kafka-backed data at {path}.")
        try:
            return api.request("GET", path, token, timeout=min(api.timeout, remaining))
        except ApiError as error:
            if (error.status, error.title) not in pending_responses:
                raise
        time.sleep(min(0.5, max(0, deadline - time.monotonic())))


def upload_design(api, project_id, architect, pdf):
    boundary = "BuildNexusPerf" + secrets.token_hex(16)
    body = (
        f"--{boundary}\r\nContent-Disposition: form-data; name=\"Name\"\r\n\r\n"
        "Performance floor plan\r\n"
        f"--{boundary}\r\nContent-Disposition: form-data; name=\"File\"; "
        "filename=\"sample-design.pdf\"\r\nContent-Type: application/pdf\r\n\r\n"
    ).encode("ascii") + pdf + f"\r\n--{boundary}--\r\n".encode("ascii")
    response = record(api.request(
        "POST", f"/api/designs/projects/{project_id}/documents", architect.token,
        expected=201, body=body, content_type=f"multipart/form-data; boundary={boundary}",
    ), "Design upload")
    require(response.get("projectId") == project_id and response.get("uploadedBy") == architect.user_id,
            "Uploaded design has incorrect project or architect ownership.")
    return identifier(response.get("id"), "Design version")


def prepare_client(api, admin_token, architect, manager, run_id, index, pdf, poll_timeout):
    client = register(api, run_id, f"client{index}")
    client.token, logged_in_id = login(api, client.email, client.password, "Client")
    require(logged_in_id == client.user_id, "Client login returned another user.")
    project = record(api.request("POST", "/api/projects", client.token, {
        "name": f"Performance House {run_id} {index}", "location": "Colombo 07",
        "landSizePerches": 20, "budget": 25000000, "floors": 2,
        "bedrooms": 3, "bathrooms": 2, "garageSpaces": 1,
        "otherRequirements": "API-prepared performance test fixture",
    }, expected=201), "Project creation")
    project_id = identifier(project.get("id"), "Project")
    require(project.get("clientId") == client.user_id, "Project belongs to another Client.")
    api.request("PUT", f"/api/projects/{project_id}/architect", admin_token,
                {"architectId": architect.user_id})
    api.request("PUT", f"/api/projects/{project_id}/project-manager", admin_token,
                {"projectManagerId": manager.user_id})

    version_id = upload_design(api, project_id, architect, pdf)
    approval = record(api.request(
        "POST", f"/api/designs/versions/{version_id}/approve", client.token,
    ), "Design approval")
    require(approval.get("status") == "Approved" and approval.get("reviewedBy") == client.user_id,
            "Design approval did not record the Client's approval.")

    summary_path = f"/api/construction/projects/{project_id}/progress-summary"
    summary = record(wait_for_read(api, summary_path, client.token, poll_timeout, {
        (403, "Not your project."), (404, "Progress not available."),
    }), "Construction plan")
    require(summary.get("projectId") == project_id, "Construction plan belongs to another project.")
    completed = record(api.request(
        "POST", f"/api/construction/projects/{project_id}/milestones", manager.token,
        {"name": "Foundation"}, expected=201,
    ), "Foundation milestone")
    milestone_id = identifier(completed.get("id"), "Foundation milestone")
    api.request("POST", f"/api/construction/projects/{project_id}/milestones", manager.token,
                {"name": "Walls"}, expected=201)
    api.request("PATCH", f"/api/construction/milestones/{milestone_id}/status", manager.token,
                {"status": "Completed"})
    summary = record(api.request("GET", summary_path, client.token), "Construction summary")
    require(summary.get("totalMilestones") == 2 and summary.get("completedMilestones") == 1
            and summary.get("progressPercent") == 50, "Construction fixture is not 50% complete.")

    # Payment Service must consume ProjectCreated before this Client can read/pay.
    invoice_path = f"/api/payments/my-projects/{project_id}/invoices"
    wait_for_read(api, invoice_path, client.token, poll_timeout, {(403, "Not your project.")})
    invoice = record(api.request(
        "POST", f"/api/payments/projects/{project_id}/invoices", manager.token,
        {"amount": 1000}, expected=201,
    ), "Invoice creation")
    invoice_id = identifier(invoice.get("id"), "Invoice")
    require(invoice.get("projectId") == project_id and invoice.get("amount") == 1000,
            "Invoice has incorrect project or amount.")
    payment = record(api.request(
        "POST", f"/api/payments/invoices/{invoice_id}/payments", client.token,
        {"amount": 250}, expected=201,
    ), "Partial payment")
    payment_record = record(payment.get("payment"), "Payment record")
    payment_id = identifier(payment_record.get("id"), "Payment")
    require(payment_record.get("paidBy") == client.user_id and payment_record.get("amount") == 250
            and payment.get("outstandingAmount") == 750, "Partial payment has incorrect ownership or balance.")
    history = record(api.request(
        "GET", f"/api/payments/my-projects/{project_id}/history", client.token,
    ), "Payment history")
    require(history.get("totalInvoiced") == 1000 and history.get("totalPaid") == 250
            and history.get("outstandingBalance") == 750, "Payment history has incorrect totals.")
    dashboard = record(api.request("GET", "/api/payments/dashboard/client", client.token), "Dashboard")
    require(dashboard.get("invoiceCount") == 1 and dashboard.get("totalDue") == 750,
            "Client dashboard does not contain the prepared outstanding invoice.")
    return dict(zip(CSV_COLUMNS, (
        client.email, client.password, client.user_id, project_id, invoice_id, payment_id,
        architect.user_id, milestone_id,
    )))


def protect_credentials(path):
    """Secure the empty temporary file before writing any credentials to it."""
    if os.name == "nt":
        # chmod does not set Windows ACLs. Use the current SID to avoid relying
        # on account-name encoding or inherited access from a shared directory.
        result = subprocess.run(["whoami", "/user", "/fo", "csv", "/nh"],
                                capture_output=True, text=True, check=True)
        sid = next(csv.reader(io.StringIO(result.stdout)))[1]
        require(sid.startswith("S-1-"), "Could not identify the Windows user for CSV permissions.")
        subprocess.run(["icacls", str(path), "/inheritance:r", "/grant:r", f"*{sid}:(F)"],
                       capture_output=True, check=True)
    else:
        os.chmod(path, 0o600)


def write_csv(output, rows):
    output.parent.mkdir(parents=True, exist_ok=True)
    descriptor, name = tempfile.mkstemp(prefix=".performance-fixtures-", suffix=".csv", dir=output.parent)
    temporary = Path(name)
    try:
        # Close before icacls/os.replace so the same path works on Windows.
        os.close(descriptor)
        protect_credentials(temporary)
        with temporary.open("w", encoding="utf-8", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=CSV_COLUMNS)
            writer.writeheader()
            writer.writerows(rows)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, output)
    finally:
        temporary.unlink(missing_ok=True)


def positive_float(value):
    number = float(value)
    if not math.isfinite(number) or number <= 0:
        raise argparse.ArgumentTypeError("must be a finite number greater than zero")
    return number


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://localhost:5000", help="API gateway origin")
    parser.add_argument("--users", type=int, default=2, help="distinct Client/project fixtures (default: 2)")
    parser.add_argument("--output", type=Path, required=True, help="private credentials CSV for JMeter")
    parser.add_argument("--timeout", type=positive_float, default=10, help="HTTP timeout in seconds (default: 10)")
    parser.add_argument("--poll-timeout", type=positive_float, default=60,
                        help="maximum wait per Kafka projection in seconds (default: 60)")
    args = parser.parse_args()
    if args.users < 1:
        parser.error("--users must be at least 1")
    origin = urllib.parse.urlsplit(args.base_url)
    if origin.scheme not in ("http", "https") or not origin.netloc or origin.username or origin.password \
            or origin.query or origin.fragment or origin.path.rstrip("/"):
        parser.error("--base-url must be an HTTP(S) origin without credentials, a path, query or fragment")
    return args


def main():
    args = parse_args()
    try:
        pdf = Path(__file__).with_name("fixtures").joinpath("sample-design.pdf").read_bytes()
        require(pdf.startswith(b"%PDF-"), "The sample design fixture is not a PDF.")
        api = Api(args.base_url, args.timeout)
        admin_token, _ = login(api, os.environ.get("ADMIN_EMAIL", "admin@buildnexus.local"),
                               os.environ.get("ADMIN_PASSWORD", "ChangeMe123!"), "Admin")
        run_id = uuid.uuid4().hex[:12]
        architect = staff_account(api, admin_token, run_id, "architect", "Architect")
        manager = staff_account(api, admin_token, run_id, "manager", "ProjectManager")
        rows = []
        for index in range(1, args.users + 1):
            rows.append(prepare_client(api, admin_token, architect, manager, run_id,
                                       index, pdf, args.poll_timeout))
            print(f"Prepared Client/project fixture {index}/{args.users}.", flush=True)
        write_csv(args.output, rows)
        print(f"Prepared {len(rows)} private CSV rows; credentials and tokens are omitted from console output.")
        return 0
    except SetupError as error:
        print(f"Fixture preparation failed: {error}", file=sys.stderr)
    except (OSError, subprocess.SubprocessError, IndexError):
        print("Fixture preparation failed: could not read the PDF or securely write the credentials CSV.",
              file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
