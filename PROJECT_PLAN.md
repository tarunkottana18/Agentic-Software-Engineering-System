# Project Improvement Plan

## In plain language

Keep the existing project. Improve it in small steps instead of starting again. First make the ordinary URL-shortener API correct and buildable. Then add tests. After that, build the agent workflow that the assignment emphasizes.

The detailed design notes are in [IMPROVEMENT_PLAN.md](IMPROVEMENT_PLAN.md). This file is the short version.

## Step 1: Make the URL shortener work

- Add the missing NuGet package references and confirm the solution builds.
- Make the short link returned by the shorten endpoint match a real redirect route.
- Create/version the SQLite database schema so a clean checkout can run.
- Reject invalid URLs, use a secure short-code generator, and retry a limited number of times if a code already exists.
- Make public URL/port settings configurable rather than hardcoding localhost.

**Done when:** a user can shorten a valid URL, open the returned link, reach the original site, and get a useful error for invalid or unknown codes.

## Step 2: Add tests

- Add a test project.
- Test shortening, duplicate URLs, redirects, missing codes, invalid URLs, and click counts.
- Use a temporary SQLite database for integration tests.
- Keep Semantic Kernel tests independent of a live AI API by using a fake implementation.

**Done when:** one documented command runs the tests successfully from a fresh checkout.

## Step 3: Build the agent workflow

The current agent endpoint sends text to a model; it does not yet manage a full software-engineering lifecycle. Build a workflow that records stages such as:

`Requirement -> Plan -> Design -> Implementation -> Tests -> Documentation -> Approval`

For each stage, record its status, inputs, outputs, dependencies, decisions, retries, and timestamps. Allow independent analysis steps to run in parallel, but wait at synchronization points before dependent work. Require a human approval before high-impact changes. Stop safely after a limited number of retries, record failures, and re-plan dependent steps when an approved requirement changes.

Use Semantic Kernel for model access and explicitly allow-listed tools. Keep approvals, policy checks, workflow state, and file-changing actions under normal application code; a prompt alone is not a safety control.

**Done when:** automated tests show a workflow can proceed, pause for approval, retry within a limit, stop on failure, resume, and re-plan after an upstream change.

## Step 4: Show the three assignment scenarios

- **Greenfield:** plan and deliver a new capability.
- **Brownfield:** change existing click analytics and demonstrate impact analysis and regression tests.
- **Ambiguous:** identify unclear requirements, ask for clarification or record assumptions, then update the plan.

For each scenario, save the requirement, task/dependency plan, approvals, validation results, and final summary as reviewable artifacts. Do not claim a scenario works until it has been run and its results recorded.

## Step 5: Keep documentation accurate

Update the README, architecture guide, functionality guide, and engineering summary whenever behavior changes. Label features as implemented, unverified, or planned. Include setup steps, test commands, risks, assumptions, and known limitations.

## Recommended design boundaries

- **Domain:** business entities and rules; no web, database, or AI framework dependencies.
- **Application:** MediatR use cases and workflow contracts; depends on abstractions.
- **Infrastructure:** SQLite persistence implementations.
- **Agents:** Semantic Kernel adapters and role prompts loaded from the root `agents/prompts/` directory.
- **API:** HTTP input/output and dependency composition; no business rules.

Use MediatR for use-case dispatch, Strategy for replaceable short-code generation, an Adapter for Semantic Kernel, and an explicit state machine/dependency graph for the SDLC workflow. Add abstractions only where they clarify a real boundary; avoid adding patterns just to increase pattern count.