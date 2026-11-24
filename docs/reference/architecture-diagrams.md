---
title: "Architecture Diagrams"
description: "Seven architecture figures showing the platform's boundaries, runtime flow, security and operating connections."
tags:
  - "architecture"
  - "diagrams"
  - "reference"
aliases:
  - "architecture diagrams"
  - "figures"
  - "system diagram"
  - "network diagram"
purpose: "reference"
status: "current"
last_verified: 2026-09-01
review_after: 2026-12-01
volatility: "medium"
source_of_truth: "The seven published architecture figures and their reader-facing descriptions"
verified_by:
  - "manual review"
common_queries:
  - "What does the system look like?"
  - "Where is the network topology diagram?"
  - "How does a bulletin flow through the system?"
see_also:
  - "docs/canonical/architecture.md"
---

# Architecture Diagrams

These seven figures show the platform's boundaries, station settings, network, security and release movement. Start with System context for scope, then use Platform architecture and Network topology to trace services and connections. The remaining figures help investigate the full bulletin lifecycle, station configuration, security control or deployment question.

## System context

![System context](diagram:system-context.png)

This figure shows the platform between ARN operators and external services. It identifies sign-in, traffic data, script generation, speech synthesis, notifications and playout boundaries. Use it when explaining what the platform includes or identifying which external service is involved in an incident.

## Platform architecture

![Platform architecture](diagram:platform-architecture.png)

This figure expands the platform boundary into the console, API and processing services, data stores, key management, AI services and monitoring. It shows how operator activity, data access, audio delivery and notifications connect. Use it when tracing a bulletin from console request to approved playout or locating a dependency.

## Bulletin lifecycle

![Bulletin state machine](diagram:bulletin-state-machine.png)

This figure, titled Bulletin Lifecycle, shows the full path from a scheduled trigger through traffic retrieval, content selection, script writing, audio recording, audio checking, operator approval and air delivery. It also shows rejection, regeneration, failure and standby-audio paths. Use it when a bulletin is waiting, needs regeneration or uses standby audio.

## Station settings

![Per-station configuration](diagram:per-station-configuration.png)

This figure shows the relationship between a Station, Scheduler, Settings in force and Bulletin generation. It explains how voice, optional voice rotation, delivery style, pronunciation dictionary, content sources, monitored roads, route priorities and editorial guidance become active for each slot. Use it when changing station settings or tracing why a bulletin used a particular voice or content source.

## Network topology

![Network topology](diagram:network-topology.png)

This figure shows the test and production topology: the console, compute and data services, private endpoints, vendor services and the ARN playout network. Development runs the same services on a public data plane with no virtual network. Use it when investigating a private endpoint, DNS, firewall, outbound service or playout connection.

## Security overview

![Security overview](diagram:security-overview.png)

This figure shows sign-in through Microsoft Entra ID, role checks for Operator and Admin, managed identity access to Azure Storage, Key Vault and Azure OpenAI, and content-safety controls. Use it when investigating sign-in or authorisation, reviewing secret access or checking a data permission.

## CI/CD pipeline

![CI/CD pipeline](diagram:cicd-pipeline.png)

This figure shows release movement through validation and promotion across development, test and production. It distinguishes deployment destinations for the console and processing services. Use it when a release is blocked or an environment promotion needs review.
