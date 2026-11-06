// Production infrastructure layer (WorkTime.Infrastructure).
//
// This project used to hold the InMemory*Repository classes. They are test fakes, so
// they now live in tests/WorkTime.Tests and are never shipped to a customer build:
// production repositories (a database-backed implementation of the interfaces in
// src/WorkTime.Application/Interfaces/IRepositories.cs) belong here instead, wired to
// the services through constructor injection.
//
// Also expected here: credential storage for BR34 (the one-way hash AuthService compares
// against) and the session / backup machinery of BR32 and BR33. No domain rule is
// re-implemented in this layer; rules stay in WorkTime.Domain.
//
// The file carries no types on its own - it documents the boundary of the layer until a
// real repository implementation is added.
