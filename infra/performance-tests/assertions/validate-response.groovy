import groovy.json.JsonSlurper

// HTTP failures already have a useful status assertion. Do not hide it with a
// JSON parsing error, and never include tokens or response bodies in failures.
if (!prev.isSuccessful()) {
    return
}

def reject = { String message ->
    AssertionResult.setFailure(true)
    AssertionResult.setFailureMessage(message)
}
def isId = { value ->
    value instanceof String && value ==~ /[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}/
}
def fixtureId = { value, String variable ->
    isId(value) && value == vars.get(variable)
}
def numberEquals = { value, expected ->
    value instanceof Number && value == expected
}

def label = prev.getSampleLabel()
def arrayLabels = ['List Projects', 'Client Invoices', 'Construction Reports']

def body
try {
    body = new JsonSlurper().parseText(prev.getResponseDataAsString())
} catch (Exception ignored) {
    reject('Expected a JSON response from the API.')
    return
}
if (label in arrayLabels ? !(body instanceof List) : !(body instanceof Map)) {
    reject(label in arrayLabels ? 'Expected a JSON array from the API.' : 'Expected a JSON object from the API.')
    return
}

def logins = [
    'Login Client': ['clientToken', 'Client'],
    'Login Architect': ['architectToken', 'Architect'],
    'Admin Login': ['adminToken', 'Admin'],
    'Login Under Load': ['journeyToken', 'Client'],
    'Admin Login For Reports': ['reportAdminToken', 'Admin']
]
if (logins.containsKey(label)) {
    def (tokenVariable, role) = logins[label]
    if (!(body.accessToken instanceof String) || body.accessToken.trim().isEmpty()
            || vars.get(tokenVariable) != body.accessToken) {
        reject('Login must return a non-empty accessToken and extract it for later requests.')
    } else if (!(body.user instanceof Map) || !isId(body.user.id) || body.user.role != role) {
        reject("Login must return a valid user ID with the ${role} role.")
    } else if (label == 'Login Under Load' && !fixtureId(body.user.id, 'client_id')) {
        reject('Login must authenticate the Client from the current fixture row.')
    }
} else if (label in ['Register Client', 'Register Architect']) {
    if (!isId(body.id) || body.role != 'Client') {
        reject('Registration must return a valid user ID and the default Client role.')
    } else if (label == 'Register Architect' && vars.get('architectId') != body.id) {
        reject('The Architect fixture ID must be extracted before Admin promotion.')
    }
} else if (label in ['Create Project', 'Create Project For Design Upload']) {
    if (!isId(body.id)) {
        reject('Project creation must return a valid project ID.')
    } else if (label == 'Create Project For Design Upload' && vars.get('projectId') != body.id) {
        reject('The design fixture project ID must be extracted for assignment and upload.')
    }
} else if (label == 'Promote Fixture To Architect') {
    if (body.id != vars.get('architectId') || body.role != 'Architect') {
        reject('Admin promotion must return the fixture user with the Architect role.')
    }
} else if (label == 'Assign Architect To Project') {
    if (body.id != vars.get('projectId') || body.assignedArchitectId != vars.get('architectId')) {
        reject('Assignment must link the fixture Architect to the fixture project.')
    }
} else if (label == 'Upload Design Document') {
    if (!isId(body.id) || body.projectId != vars.get('projectId')
            || body.uploadedBy != vars.get('architectId') || body.contentType != 'application/pdf') {
        reject('Upload must return a PDF version for the fixture project and Architect.')
    }
} else if (label == 'List Projects') {
    if (!body.any { project ->
        project instanceof Map && fixtureId(project.id, 'project_id')
            && fixtureId(project.clientId, 'client_id')
    }) {
        reject('The project list must contain the fixture project owned by the fixture Client.')
    }
} else if (label == 'View Project') {
    if (!fixtureId(body.id, 'project_id') || !fixtureId(body.clientId, 'client_id')
            || !(body.statusHistory instanceof List) || body.statusHistory.isEmpty()) {
        reject('Project detail must return the fixture project, its Client and a populated status history.')
    }
} else if (label in ['Client Invoices', 'Payment History', 'Payment Dashboard']) {
    // Check the seeded ledger, rather than accepting a fast but empty response.
    // Each fixture owns one invoice for 1000 and one partial payment of 250.
    def invoiceMatches = { invoice ->
        invoice instanceof Map && fixtureId(invoice.id, 'invoice_id')
            && fixtureId(invoice.projectId, 'project_id')
            && numberEquals(invoice.amount, 1000) && invoice.status == 'Pending'
    }
    def paymentMatches = { payment ->
        payment instanceof Map && fixtureId(payment.id, 'payment_id')
            && fixtureId(payment.invoiceId, 'invoice_id')
            && fixtureId(payment.paidBy, 'client_id') && numberEquals(payment.amount, 250)
    }

    if (label == 'Client Invoices') {
        if (body.size() != 1 || !invoiceMatches(body[0])) {
            reject('The invoice list must return the fixture invoice for 1000 with Pending status.')
        }
    } else if (label == 'Payment History') {
        def invoices = body.invoices
        if (!fixtureId(body.projectId, 'project_id')
                || !numberEquals(body.totalInvoiced, 1000) || !numberEquals(body.totalPaid, 250)
                || !numberEquals(body.outstandingBalance, 750)
                || !(invoices instanceof List) || invoices.size() != 1
                || !(invoices[0] instanceof Map) || !invoiceMatches(invoices[0].invoice)
                || !numberEquals(invoices[0].amountPaid, 250)
                || !numberEquals(invoices[0].outstandingAmount, 750)
                || !(invoices[0].payments instanceof List) || invoices[0].payments.size() != 1
                || !paymentMatches(invoices[0].payments[0])) {
            reject('Payment history must contain the fixture invoice and payment with totals 1000, 250 and 750.')
        }
    } else {
        def invoices = body.invoices
        if (!numberEquals(body.totalDue, 750) || !numberEquals(body.invoiceCount, 1)
                || !(invoices instanceof List) || invoices.size() != 1
                || !(invoices[0] instanceof Map) || !fixtureId(invoices[0].invoiceId, 'invoice_id')
                || !fixtureId(invoices[0].projectId, 'project_id')
                || !numberEquals(invoices[0].amount, 1000) || !numberEquals(invoices[0].amountPaid, 250)
                || !numberEquals(invoices[0].outstandingAmount, 750)) {
            reject('The payment dashboard must contain the fixture invoice with 250 paid and 750 due.')
        }
    }
} else if (label == 'Construction Progress') {
    def milestones = body.milestones
    if (!fixtureId(body.projectId, 'project_id') || !numberEquals(body.totalMilestones, 2)
            || !numberEquals(body.completedMilestones, 1) || !numberEquals(body.progressPercent, 50)
            || body.phase != null || !(milestones instanceof List) || milestones.size() != 2
            || !milestones.every { milestone ->
                milestone instanceof Map && isId(milestone.id) && fixtureId(milestone.projectId, 'project_id')
            }
            || !milestones.any { milestone ->
                fixtureId(milestone.id, 'milestone_id') && milestone.status == 'Completed'
            }
            || milestones.count { milestone -> milestone.status == 'NotStarted' } != 1) {
        reject('Construction progress must contain the two fixture milestones, one completed, at 50 percent.')
    }
} else if (label == 'Construction Reports') {
    if (!body.any { row ->
        row instanceof Map && fixtureId(row.projectId, 'project_id')
            && numberEquals(row.totalMilestones, 2) && numberEquals(row.completedMilestones, 1)
            && numberEquals(row.inProgressMilestones, 0) && numberEquals(row.notStartedMilestones, 1)
            && numberEquals(row.progressPercent, 50) && row.phaseStatus == null
    }) {
        reject('The construction report must include the fixture project with one of two milestones complete.')
    }
}
