# Documentation

This directory holds all project documentation. Start with the
[root README](../README.md) for an overview, then use this index to find the
detail you need.

| Document | Purpose |
| --- | --- |
| [DEVELOPMENT.md](DEVELOPMENT.md) | Environment setup, common commands, testing approach, verification log |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Layers, module boundaries, dependency rules, key design decisions |
| [PROJECT-STRUCTURE.md](PROJECT-STRUCTURE.md) | Repository map: what lives where, where new code belongs, naming conventions |
| [DATABASE.md](DATABASE.md) | Schema, relationships, migrations, initialization rules |
| [SECURITY.md](SECURITY.md) | Authentication, authorization, validation, audit, secrets handling |
| [security/SECURITY_AUDIT.md](security/SECURITY_AUDIT.md) | Evidence-based security audit: findings, fixes, tests, residual risks |
| [DEPLOYMENT.md](DEPLOYMENT.md) | Native and container deployment, configuration, hardening checklist |
| [DESIGN.md](DESIGN.md) | UI/UX design system: colors, typography, spacing, components, appearance rules |
| [DEMO.md](DEMO.md) | Fictional demo data inventory and presentation walkthrough |

## Reading order

- **New contributor / new to the codebase** — [DEVELOPMENT.md](DEVELOPMENT.md),
  then [ARCHITECTURE.md](ARCHITECTURE.md) and
  [PROJECT-STRUCTURE.md](PROJECT-STRUCTURE.md).
- **Making a change** — [PROJECT-STRUCTURE.md](PROJECT-STRUCTURE.md) tells you
  where code belongs; [ARCHITECTURE.md](ARCHITECTURE.md) lists the layer rules
  your change must respect.
- **Touching the data model** — [DATABASE.md](DATABASE.md) (never enable
  automatic migrations or destructive operations at startup).
- **Touching UI** — [DESIGN.md](DESIGN.md) defines the visual language the
  styles in `src/Emplyo.Web/wwwroot/css/emplyo.css` implement.
- **Deploying** — [DEPLOYMENT.md](DEPLOYMENT.md), including the hardening
  checklist.
- **Demonstrating the product** — [DEMO.md](DEMO.md).
