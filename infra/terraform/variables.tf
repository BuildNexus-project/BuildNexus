# Input variables for the BuildNexus Azure stack.
#
# Nothing secret has a default. A value left undefined makes Terraform stop and
# ask rather than quietly applying a placeholder, and the real values are passed
# in through TF_VAR_* environment variables or a git-ignored terraform.tfvars —
# see terraform.tfvars.example and infra/RUNBOOK.md.

variable "subscription_id" {
  description = "Azure subscription the stack is created in. Left null, the azurerm provider reads ARM_SUBSCRIPTION_ID and then falls back to the subscription the Azure CLI has selected, which is how this is normally run."
  type        = string
  default     = null
}
