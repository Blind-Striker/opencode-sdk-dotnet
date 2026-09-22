namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// One contender as the election iteration sees it: finished or live, whether it exited 0, and the
/// failure to report when there is one. No process handles; the ensurer maps
/// <see cref="ServiceContender"/> onto this before each <see cref="ServiceElectionIteration"/>.
/// </summary>
/// <param name="Finished">Whether the contender is finished the way upstream's <c>contenderFinished</c> means it.</param>
/// <param name="ExitedZero">Whether a poll has observed exit code 0.</param>
/// <param name="Failure">The pinned client's <c>contenderFailure</c>, or null.</param>
internal sealed record ServiceContenderObservation(bool Finished, bool ExitedZero, OpenCodeServerException? Failure);
