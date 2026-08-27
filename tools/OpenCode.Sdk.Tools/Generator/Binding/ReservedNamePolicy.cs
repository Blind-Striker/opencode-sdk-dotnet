using System.Collections.Frozen;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Single owner of every name the emitted surface claims for itself. The binder refuses wire
/// names landing on these sets, and the emitters render the parameter names from the same
/// constants, so the two sides cannot drift apart silently; the reflection tests assert the
/// type and member sets against the hand-written <c>OpenCode.Sdk</c> surface.
/// </summary>
internal static class ReservedNamePolicy
{
    /// <summary>Parameter name every emitted operation method appends for cancellation.</summary>
    public const string CancellationTokenParameter = "cancellationToken";

    /// <summary>Local and argument name an emitted operation method uses for its declared headers.</summary>
    public const string DeclaredHeadersParameter = "declaredHeaders";

    /// <summary>Parameter name emitted constructors take for the shared pipeline spine.</summary>
    public const string PipelineParameter = "pipeline";

    /// <summary>Parameter name emitted methods and route builders use for the operation request.</summary>
    public const string RequestParameter = "request";

    /// <summary>Parameter name emitted methods append for per-call options.</summary>
    public const string RequestOptionsParameter = "requestOptions";

    /// <summary>
    /// Hand-written public spine types in the generated namespaces; a generated twin would not
    /// compile. The PTY family's two public doors are here because ADR-0021 gives the family's
    /// public surface to hand-written code while the generator keeps its raw twins.
    /// </summary>
    public static readonly FrozenSet<string> SpineTypeNames = FrozenSet.ToFrozenSet(
        [
            "ErrorBehavior",
            "IOpenCodeClientOptions",
            EffectStreamTypeNamePolicy.CauseMarkerInterface,
            "ListCursor",
            "ListOrder",
            "ListRequest",
            "LocationSelector",
            "OpenCodeApiException",
            "OpenCodeClientOptions",
            "OpenCodeException",
            "OpenCodeRequestOptions",
            "OpenCodeResponse",
            "OpenCodeStreamFailureException",
            "OpenCodeTransportException",
            "PtyClient",
            "PtysClient",
            "QueryBoolean",
            "SessionParentFilter",
        ],
        StringComparer.Ordinal);

    /// <summary>Parameter names the emitted method and route-builder signatures append themselves.</summary>
    public static readonly FrozenSet<string> ParameterNames = FrozenSet.ToFrozenSet(
        [
            CancellationTokenParameter,
            DeclaredHeadersParameter,
            PipelineParameter,
            RequestParameter,
            RequestOptionsParameter,
        ],
        StringComparer.Ordinal);

    /// <summary>
    /// Member names every generated response envelope inherits from the response spine or
    /// from <see cref="object"/>/record synthesis; a payload landing on one of them needs a
    /// curated override.
    /// </summary>
    public static readonly FrozenSet<string> PayloadNames = FrozenSet.ToFrozenSet(
        [
            "Cursor",
            "Deconstruct",
            "EqualityContract",
            "Equals",
            "Error",
            "GetHashCode",
            "GetType",
            "IsError",
            "Location",
            "MemberwiseClone",
            "PrintMembers",
            "RawBody",
            "ReferenceEquals",
            "Status",
            "ToString",
        ],
        StringComparer.Ordinal);
}
