using System.Runtime.CompilerServices;

namespace OpenCode.Sdk.Internal.Pagination;

/// <summary>Lazily traverses generated cursor-list operations through their ordinary one-page methods.</summary>
internal static class CursorPaginator
{
    /// <summary>Creates a lazy item sequence over the operation's opaque next cursors.</summary>
    public static IAsyncEnumerable<TItem> EnumerateAsync<TRequest, TResponse, TItem>(
        Func<TRequest?, OpenCodeRequestOptions?, CancellationToken, Task<TResponse>> fetchPage,
        TRequest? initialRequest,
        ICursorPageAdapter<TRequest, TResponse, TItem> adapter,
        CancellationToken cancellationToken)
        where TRequest : ListRequest
        where TResponse : OpenCodeResponse
    {
        ArgumentNullException.ThrowIfNull(fetchPage);
        ArgumentNullException.ThrowIfNull(adapter);

        return EnumerateCoreAsync(fetchPage, initialRequest, adapter, cancellationToken);
    }

    private static async IAsyncEnumerable<TItem> EnumerateCoreAsync<TRequest, TResponse, TItem>(
        Func<TRequest?, OpenCodeRequestOptions?, CancellationToken, Task<TResponse>> fetchPage,
        TRequest? initialRequest,
        ICursorPageAdapter<TRequest, TResponse, TItem> adapter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TRequest : ListRequest
        where TResponse : OpenCodeResponse
    {
        var request = initialRequest;
        while (true)
        {
            var page = await fetchPage(request, null, cancellationToken).ConfigureAwait(false);
            foreach (var item in adapter.GetItems(page))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }

            if (adapter.GetNextCursor(page) is not { } cursor)
            {
                yield break;
            }

            request = adapter.CreateNextRequest(initialRequest, cursor);
        }
    }
}
