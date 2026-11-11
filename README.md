# 🎓 High-Concurrency School Admission & Real-Time Seat Allocation Platform

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![PostgreSQL 16](https://img.shields.io/badge/PostgreSQL-16-336791?logo=postgresql)](https://www.postgresql.org/)
[![React 19](https://img.shields.io/badge/React-19-61DAFB?logo=react)](https://react.dev/)
[![Tailwind CSS](https://img.shields.io/badge/Tailwind-3.4-38B2AC?logo=tailwind-css)](https://tailwindcss.com/)
[![Docker Compose](https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker)](https://www.docker.com/)
[![Tests](https://img.shields.io/badge/Tests-28%2F28%20Passed-success)](#-automated-testing--empirical-verification)
[![Port 8080](https://img.shields.io/badge/API%20Port-8080-brightgreen)](#-quick-start-guide)

An enterprise-grade, race-condition-free admission platform engineered to handle extreme burst traffic during public school enrollment. Designed strictly following **Clean Architecture**, **Domain-Driven Design (DDD)**, and **PostgreSQL Native Row-Level Locking (`FOR UPDATE SKIP LOCKED`)**, coupled with an intuitive **React 19** live matrix synchronized via **3-second short polling**.

---

## 📑 Table of Contents
- [Architectural Blueprint](#-architectural-blueprint)
- [The Core Concurrency Problem & Native DB Solution](#-the-core-concurrency-problem--native-db-solution)
- [120-Second Hold State Machine & Sweeper](#-120-second-hold-state-machine--sweeper)
- [Zero 500 Error Rule & RFC 7807 Standard](#-zero-500-error-rule--rfc-7807-standard)
- [Frontend Virtual DOM & Short Polling Topology](#-frontend-virtual-dom--short-polling-topology)
- [Automated Testing & Empirical Verification (28/28)](#-automated-testing--empirical-verification-2828)
- [Quick Start Guide (Docker Compose & Local IDE)](#-quick-start-guide)
- [API Specification & Dual-Compatibility Endpoints](#-api-specification--dual-compatibility-endpoints)
- [Architecture & AI Usage Documentation](#-architecture--ai-usage-documentation)

---

## 🏛️ Architectural Blueprint

The platform strictly isolates business rules from infrastructure and presentation concerns using Clean Architecture:

```
                            ┌───────────────────────────────────┐
                            │        Presentation Layer         │
                            │      React 19 UI (Port 80/5173)   │
                            └─────────────────┬─────────────────┘
                                              │ (HTTP REST / JSON)
                            ┌─────────────────▼─────────────────┐
                            │        Web API Controllers        │
                            │      .NET 9 Kestrel (Port 8080)   │
                            │ (ClassesController & ContractApi) │
                            └─────────────────┬─────────────────┘
                                              │
                    ┌─────────────────────────▼─────────────────────────┐
                    │            Application Layer (Contracts)          │
                    │   Immutable DTOs | FluentValidation | ISeatService│
                    └─────────────────────────┬─────────────────────────┘
                                              │
        ┌─────────────────────────────────────┴─────────────────────────────────────┐
        │                                                                           │
┌───────▼──────────────────────────┐                       ┌────────────────────────▼──────────────────────────┐
│           Domain Layer           │                       │               Infrastructure Layer                │
│ Rich Entities (Class, Seat)      │                       │ EF Core 9 | Row-Locking Services | Expiry Worker  │
│ Strict State Machine Invariants  │                       │ PostgreSQL 16 (Native SKIP LOCKED) | Dev SQLite   │
└──────────────────────────────────┘                       └───────────────────────────────────────────────────┘
```

---

## ⚡ The Core Concurrency Problem & Native DB Solution

### The Challenge
During admission openings, thousands of parents attempt to hold the exact same remaining seats simultaneously. Naive implementations result in:
1. **Overbooking:** Two applicants receiving the same seat number.
2. **Deadlocks:** Concurrent database transactions locking each other.
3. **Application Crashes:** Unhandled exceptions bubbling up to the client.

### The Architectural Solution
Rather than adding distributed state bloat (e.g., Redis/Redlock), we solve concurrency natively at the PostgreSQL engine level:

```sql
SELECT * FROM "Seats"
WHERE "ClassId" = @classId AND "Status" = 0
ORDER BY "SeatNumber" ASC
LIMIT 1
FOR UPDATE SKIP LOCKED;
```

* **Zero Deadlocks:** Transactions bypass locked rows instantly without waiting.
* **$O(1)$ Time Complexity:** First available row locked directly by the DB kernel.
* **Mathematically Guaranteed:** It is physically impossible for two concurrent requests to acquire the same seat.

---

## ⏱️ 120-Second Hold State Machine & Sweeper

Each seat follows a strict deterministic lifecycle:

```
                      ┌───────────────┐
                      │   Available   │◄──────────────────────────┐
                      └───────┬───────┘                           │
                              │ POST /classes/{id}/holds          │
                              ▼                                   │
                      ┌───────────────┐                           │
       ┌──────────────│     Held      │                           │
       │              └───────┬───────┘                           │
       │                      │ POST /holds/{id}/confirm          │
       │                      ▼                                   │
       │              ┌───────────────┐                           │
       │              │   Confirmed   │                           │
       │              └───────────────┘                           │
       │                                                          │
       │ 120s Expiry (SeatExpiryWorker) OR DELETE /holds/{id}     │
       └──────────────────────────────────────────────────────────┘
```

* **Hold Duration:** Exactly 120 seconds (2 minutes).
* **Sweeper Interval:** Background worker runs every 5 seconds.
* **Optimized Sweeper Index:**
  ```sql
  CREATE INDEX "IX_Seats_ExpiresAtUtc_Held" ON "Seats" ("ExpiresAtUtc") WHERE "Status" = 1;
  ```
  Sub-millisecond query execution time regardless of total seat volume.

---

## 🛡️ Zero 500 Error Rule & RFC 7807 Standard

Client applications never receive raw 500 errors or unhandled stack traces. All failures are intercepted by `ExceptionHandlingMiddleware` and mapped to RFC 7807 ProblemDetails:

| Scenario | HTTP Status | ProblemDetails Title |
| :--- | :--- | :--- |
| Invalid 17-digit Birth ID | `400 Bad Request` | Validation Error |
| Class or Seat does not exist | `404 Not Found` | Resource Not Found |
| Seat already taken by another applicant | `409 Conflict` | Seat State Conflict |
| Student already holds a seat in this class | `409 Conflict` | Duplicate Student Booking |
| Hold expired before confirmation | `409 Conflict` | Seat State Conflict |

---

## 🖥️ Frontend Virtual DOM & Short Polling Topology

* **React 19 + TypeScript + Vite + Tailwind CSS**
* **3-Second Short Polling:** Periodic lightweight HTTP GET requests keep the seat matrix synchronized without stateful WebSocket overhead.
* **Virtual DOM Diffing:** React reconciles seat array diffs in memory and updates only the tiles whose status changed, ensuring zero screen flicker.
* **Instant Client Hold Release:** Allows parents to manually release a held seat immediately via `DELETE` without waiting for the 120s timer to expire.

---

## 🧪 Automated Testing & Empirical Verification (28/28)

Every invariant is verified by automated tests running under xUnit and FluentAssertions:

```bash
dotnet test SchoolAdmission.sln
```

```
Passed! - Failed: 0, Passed: 28, Skipped: 0, Total: 28, Duration: 3.1 s
```

### Key Test Highlights:
* **`SeatConcurrencyStressTests.cs`:** 20 parallel threads simultaneously compete for 3 remaining seats. Exactly 3 succeed; exactly 17 receive clean 409 Conflict responses. Total allocated seats remain exactly 3. **Zero overbooking.**
* **`AdmissionContractTests.cs`:** Full lifecycle validation across all 8 REST endpoints, asserting `X-Student-Id` header handling and strict **204 NoContent** response on hold cancellations.
* **Domain Invariant Tests:** Encapsulation tests proving illegal transitions (e.g., Available $\rightarrow$ Confirmed directly) throw exceptions.
* **FluentValidation Tests:** Validating 17-digit birth ID rules (`^[0-9]{17}$`).

---

## 🚀 Quick Start Guide

### Option 1: Docker Compose (Full Stack - Recommended)

```bash
# Clone the repository
git clone https://github.com/tahsin469/School-Admission-System.git
cd School-Admission-System

# Start all services (Database, .NET 9 API, React 19 UI)
docker compose up --build
```

* **Web UI:** `http://localhost` (Port 80)
* **Backend API Swagger:** `http://localhost:8080/swagger` (Port 8080)
* **PostgreSQL:** Port 5432

---

### Option 2: Local Development (IDE / CLI)

**Backend (.NET 9):**
```bash
cd backend/src/SchoolAdmission.Api
dotnet run
```
*API runs automatically on `http://localhost:8080`.*
*(Features smart fallback to local physical SQLite if local PostgreSQL is not running).*

**Frontend (React 19):**
```bash
cd frontend
npm install
npm run dev
```
*Frontend runs on `http://localhost:5173`.*

---

## 📮 API Specification & Dual-Compatibility Endpoints

The API is engineered with **Dual-Compatibility**: supporting both standard UI resource interactions and automated external test suites.

| # | HTTP Method | Path | Header | Success Status | Description |
| :---: | :--- | :--- | :--- | :---: | :--- |
| **1** | `POST` | `/classes` | — | `201 Created` | Create new admission class quota |
| **2** | `GET` | `/classes` | — | `200 OK` | List all classes with live seat counts |
| **3** | `GET` | `/classes/{classId}` | — | `200 OK` | Fetch detailed class quota metrics |
| **4** | `POST` | `/classes/{classId}/holds` | `X-Student-Id` | `201 Created` | Hold next available seat (409 if full) |
| **5** | `GET` | `/holds/{holdId}` | — | `200 OK` | Check hold status and expiry |
| **6** | `POST` | `/holds/{holdId}/confirm` | `X-Student-Id` | `200 OK` | Confirm admission upon fee payment |
| **7** | `DELETE` | `/holds/{holdId}` | `X-Student-Id` | `204 NoContent` | Instantly release seat hold back to open |
| **8** | `GET` | `/me/holds` | `X-Student-Id` | `200 OK` | Query all active/historical holds for student |

A complete Postman collection is included in the root directory: [`SchoolAdmission.postman_collection.json`](./SchoolAdmission.postman_collection.json).

---

## 📚 Architecture & AI Usage Documentation

For an in-depth audit of architectural choices and engineering reflections, please review:
* [**`AI_USAGE.md`**](./AI_USAGE.md) - Engineering reflection, human architectural judgment, why naive AI suggestions were rejected, and empirical verification proof.
* [**`ASSUMPTIONS.md`**](./ASSUMPTIONS.md) - In-depth trade-off analysis of native DB locks vs. distributed locks, 120s hold lifecycle, and RFC 7807 error boundaries.

---

**Author:** Tahsin  
**Repository:** [github.com/tahsin469/School-Admission-System](https://github.com/tahsin469/School-Admission-System)
