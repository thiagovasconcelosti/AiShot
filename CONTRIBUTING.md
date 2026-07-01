# Contributing to AiShot

Thanks for your interest in improving AiShot! This is a Windows-only .NET 10 WinForms app.

## Getting started

```sh
git clone https://github.com/thiagovasconcelosti/AiShot.git
cd AiShot
dotnet build src/AiShot/AiShot.csproj -c Debug
dotnet run --project src/AiShot/AiShot.csproj
```

Requirements: Windows, .NET 10 SDK. See the [technical docs](https://thiagovasconcelosti.github.io/AiShot/#/technical) for architecture and the build/publish commands.

## Workflow

1. Create a branch from `master`: `fix/…`, `feat/…`, `docs/…`, `refactor/…`.
2. Make your change; keep it focused and matching the surrounding code style.
3. Build and test manually (capture, editor, AI chat, settings — whatever you touched).
4. Open a Pull Request against `master` and fill in the template.

Direct pushes to `master` are allowed for the maintainer, but force-push and branch deletion are blocked by a ruleset.

## Commit messages

Use [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `refactor:`, `chore:`, `build:`.

- **Never commit secrets** (API keys, `%APPDATA%` config). The real `appsettings.json` is gitignored.
- **Do not add AI co-author trailers** to commits.

## Reporting

- **Bugs / features:** open an issue using the templates.
- **Security vulnerabilities:** report privately via [Security advisories](https://github.com/thiagovasconcelosti/AiShot/security/advisories/new) — see [SECURITY.md](SECURITY.md). Do not open a public issue.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
