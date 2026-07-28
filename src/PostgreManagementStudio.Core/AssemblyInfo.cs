using System.Runtime.CompilerServices;

// Sprint 002: IResultSetWriter is internal to Core so the public Core API does not
// expose arbitrary row mutation. The Results implementation in this assembly is the
// sole allowed implementer; expose only that assembly.
[assembly: InternalsVisibleTo("PostgreManagementStudio.Results")]
[assembly: InternalsVisibleTo("PostgreManagementStudio.Results.Tests")]
[assembly: InternalsVisibleTo("PostgreManagementStudio.IntegrationTests")]