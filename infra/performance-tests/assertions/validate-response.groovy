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

def body
try {
    body = new JsonSlurper().parseText(prev.getResponseDataAsString())
} catch (Exception ignored) {
    reject('Expected a JSON response from the API.')
    return
}
if (!(body instanceof Map)) {
    reject('Expected a JSON object from the API.')
    return
}

def label = prev.getSampleLabel()
def logins = [
    'Login Client': ['clientToken', 'Client'],
    'Login Architect': ['architectToken', 'Architect'],
    'Admin Login': ['adminToken', 'Admin']
]
if (logins.containsKey(label)) {
    def (tokenVariable, role) = logins[label]
    if (!(body.accessToken instanceof String) || body.accessToken.trim().isEmpty()
            || vars.get(tokenVariable) != body.accessToken) {
        reject('Login must return a non-empty accessToken and extract it for later requests.')
    } else if (!(body.user instanceof Map) || !isId(body.user.id) || body.user.role != role) {
        reject("Login must return a valid user ID with the ${role} role.")
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
}
