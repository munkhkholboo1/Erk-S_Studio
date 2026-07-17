# Erk-S Relationship Boundary

Status: product architecture baseline, not a legal opinion or final terms of service.

## Purpose

Erk-S supports Mongolia's design-project workflow without becoming a contracting party, employer,
professional guarantor, file custodian, dispute resolver, or representative of any participant.
The same boundary applies to clients, design organizations, individual professionals, contractors,
planning authorities, reviewers, and supervisors.

## Neutrality

- The platform applies the same role and scope rules to every party.
- It does not disclose private organization or participant data to give one party an advantage.
- It does not decide contractual, payment, authorship, employment, professional-quality, or liability disputes.
- A platform approval records a workflow decision only. It is not a digital signature, statutory permit,
  professional-quality warranty, or proof that an off-platform contract was performed.

## Platform Responsibilities

Neutrality does not remove responsibility for Erk-S's own system behavior. The platform remains
responsible for implementing and operating:

- account and device authentication;
- role- and scope-based access control;
- invitation, consent, notification, and organization-approval workflows;
- accurate, tamper-evident workflow and acknowledgement audit records;
- reasonable security, privacy, availability, backup, and recovery controls for data it stores;
- data minimization and authorized disclosure;
- clear separation between cloud metadata and local native design files.

## Responsibilities That Stay With The Parties

The parties arrange and document outside Erk-S:

- contracts, employment, payment, tax, insurance, and professional liability;
- copyright, authorship, licenses, confidentiality, and reuse rights;
- scope, schedule, acceptance criteria, quality, and deliverable handover;
- statutory permits, professional licenses, signatures, and official submissions;
- transfer, backup, retention, and recovery of RVT, DWG, and other native source files;
- negotiation, mediation, arbitration, litigation, and other dispute resolution.

## Native Source Files

Erk-S does not upload, store, transmit, or take custody of RVT, DWG, or other professional native
files. A project stores only the source identity, local binding metadata, manifest, hash, sheet
metadata, document, report, audit record, and PDF output needed by the platform workflow.

When custody changes, Studio changes the cloud source custodian and allows the new participant to
bind a local replacement path. The parties perform the actual native-file handover outside the
platform. Erk-S must never imply that metadata reassignment proves file delivery or transfers
ownership or usage rights.

## Relationship-Changing Actions

The current policy version must be explicitly acknowledged before:

- inviting or accepting a project member;
- removing a member or requesting, approving, or declining a project exit;
- issuing or redeeming a one-time organization project-creation grant;
- creating a project for a client or assigning a design organization;
- transferring cloud source custody.

The server rejects protected API calls without the current policy acknowledgement. Accepted actions
record the actor, action, counterparty reference, policy version, and timestamp in project audit data.

## Offboarding

- Removing an active member requires a warning and leaves their native files untouched.
- A member's exit is a request, not an immediate deletion. The project creator organization must
  decide it and receives a notification.
- Sources assigned to a departing member become unassigned until an authorized manager appoints a
  new edit-capable custodian.
- Offboarding does not settle payment, authorship, confidentiality, handover, or other disputes.
- Project records and audit history are not silently rewritten to erase prior participation.

## Before Public Release

Mongolian legal counsel should review the final Terms of Service, Privacy Policy, data-retention and
deletion rules, security incident process, evidence and audit wording, complaint process, and the
exact language shown for every relationship-changing action. Product text must not promise immunity
from Erk-S's own security, privacy, access-control, or operational failures.
