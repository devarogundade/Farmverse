using System.Runtime.CompilerServices;

// Tests may reach internal seams (e.g. clock sampling) without widening the public API.
[assembly: InternalsVisibleTo("Farmverse.Tests.EditMode")]
