# Project Errand

## Overview
Errand is a next-generation, high-performance Mediator pattern implementation for .NET 8+. 
The name "Errand" symbolizes a mission of great importance—delivering a message from a sender to a specific destination with speed and reliability.

## Core Philosophy
1. **Swift Delivery (Zero Reflection):** Errand must use C# Source Generators to map Requests to Handlers at compile-time. We eliminate the startup cost and runtime overhead of reflection.
2. **Reliable Messengers (Type Safety):** If an Errand (Request) has no registered fulfillment (Handler), the project must fail to compile. No more runtime "Handler not found" exceptions.
3. **Lightweight Gear:** Focus on minimal heap allocations. Default to `ValueTask` for asynchronous operations to support high-throughput scenarios.
4. **Native AOT Ready:** Absolute compatibility with Native AOT to support modern cloud-native architectures.