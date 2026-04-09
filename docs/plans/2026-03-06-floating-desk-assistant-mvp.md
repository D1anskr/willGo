# Floating Desk Assistant MVP Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a runnable Windows desktop MVP with floating ball, translucent chat window, screenshot+text Q&A, model failover, settings persistence, and logging.

**Architecture:** Single WPF app on .NET 8 with strict folder layering (`UI` / `Application` / `Infrastructure` / `Configuration`). UI uses MVVM view models and async commands. Model requests are routed through retry + primary/secondary failover, while configuration and logs persist under LocalAppData.

**Tech Stack:** C# 12, .NET 8, WPF, WinForms overlay for screenshot selection.

---

### Task 1: Project bootstrap and layered folders
- Create WPF project targeting `net8.0-windows10.0.19041.0`
- Enable `UseWPF`, `UseWindowsForms`, `PerMonitorV2` manifest
- Create top-level folders and base model classes

### Task 2: Floating ball and chat window
- Build `BallWindow` with transparency, hover opacity, drag and edge snapping
- Build `ChatWindow` with translucent shell and opaque text rendering
- Implement chat input shortcuts and scroll behavior

### Task 3: Screenshot capture workflow
- Implement full-screen overlay form
- Support drag region selection and confirmation panel
- Return PNG bytes and auto-send from chat flow

### Task 4: Model API and failover
- Build async HTTP client for text/image mixed messages
- Classify retryable/non-retryable failures
- Implement exponential backoff and primary->secondary failover

### Task 5: Config + secure key persistence
- Implement load/save service under LocalAppData
- Protect API keys with DPAPI
- Add settings window and restore-default function

### Task 6: Error handling and logging
- Add app-level unhandled exception hook
- Add user-visible error/status messages
- Add file logger with sensitive token masking

### Task 7: Verification and delivery
- Verify file consistency and run available checks
- Produce build/publish instructions and acceptance checklist
