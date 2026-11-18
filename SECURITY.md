# Security policy

## Supported versions

The latest tagged release on `main` is the supported version.

## Reporting a vulnerability

Please **do not** open a public issue for a security problem. Use GitHub's private vulnerability reporting on this repository, or contact the maintainers through the profile linked from the repository.

Include:

- a description of the issue and its impact
- steps to reproduce (endpoints, payloads, or screens)
- the affected version/commit

You should receive an acknowledgement within a few days. Please allow time for a fix and release before public disclosure.

## Deployment notes

For real deployments: set a strong `JWT_SECRET`, change `POSTGRES_PASSWORD`, set `SEED_DEMO_DATA=false`, restrict `ALLOWED_ORIGINS`, set `ADMIN_EMAILS`, leave `ENABLE_SWAGGER=false`, and terminate TLS in front of nginx. See the Production checklist in the README.
