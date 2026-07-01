# Security Policy

## Reporting a vulnerability

Please **do not** open a public issue for security vulnerabilities.

Report privately through GitHub Security Advisories:
👉 https://github.com/thiagovasconcelosti/AiShot/security/advisories/new

Include a description, steps to reproduce, and the affected version. You'll get a response as soon as possible.

## Supported versions

The latest released version is supported. Older versions are not maintained.

## Handling of credentials

AiShot stores AI provider API keys **encrypted at rest** using Windows DPAPI
(`DataProtectionScope.CurrentUser`) in `%APPDATA%\AiShot\appsettings.json`.
Keys are never written in plain text, and configuration files are gitignored.
Environment variables (`AISHOT_*`) can be used to avoid touching disk entirely.

If you find keys or secrets committed anywhere in this repository, please report it
privately as described above.
