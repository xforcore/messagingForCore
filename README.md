# messagingForCore

messagingForCore is Microsoft's `System.Messaging`, from the .NET Framework 4.8 Reference Source, ported to .NET 10 so
that applications and libraries written for MSMQ run unchanged on Linux, Windows and macOS: local private queues are
stored in a SQLite database that the host prepares, with `XmlMessageFormatter` and `BinaryMessageFormatter`, and
nothing calls MSMQ. The design, its scope and the names are in
[issue #1](https://github.com/xforcore/messagingForCore/issues/1); the work lands through pull requests into `main`.
