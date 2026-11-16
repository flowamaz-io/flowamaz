namespace Flowamaz.Core.Git;

/// <summary>A person credited on a workflow commit: the saver as author, collaborators as Co-authored-by trailers.</summary>
public sealed record CommitAuthor(string Name, string Email);
