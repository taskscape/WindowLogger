# WindowLogger .NET 10 Upgrade Tasks

## Overview

This document tracks the execution of the WindowLogger solution upgrade to .NET 10.0. The WindowLoggerConfigGui project will be upgraded from .NET Framework 4.8 to .NET 10.0-windows, with all changes applied in a single atomic operation.

**Progress**: 1/2 tasks complete (50%) ![50%](https://progress-bar.xyz/50)

---

## Tasks

### [✓] TASK-001: Verify prerequisites *(Completed: 2026-02-13 12:36)*
**References**: Plan §Migration Strategy, Plan §Risk Management

- [✓] (1) Verify Windows Desktop workload is available for .NET 10.0 SDK
- [✓] (2) Windows Desktop workload is available (**Verify**)

---

### [▶] TASK-002: Atomic framework and package upgrade
**References**: Plan §Project-by-Project Plans - WindowLoggerConfigGui, Plan §Package Update Reference, Plan §Breaking Changes Catalog

- [ ] (1) Update WindowLoggerConfigGui target framework to net10.0-windows with Windows Desktop support per Plan §Project-by-Project Plans
- [ ] (2) Update System.Text.Json package from 10.0.2 to 10.0.3 in WindowLoggerConfigGui per Plan §Package Update Reference
- [ ] (3) Restore dependencies for entire solution
- [ ] (4) Build solution and fix all compilation errors per Plan §Breaking Changes Catalog
- [ ] (5) Solution builds with 0 errors (**Verify**)
- [ ] (6) Commit changes with message: "TASK-002: Upgrade WindowLoggerConfigGui to .NET 10.0 and update System.Text.Json to 10.0.3"

---