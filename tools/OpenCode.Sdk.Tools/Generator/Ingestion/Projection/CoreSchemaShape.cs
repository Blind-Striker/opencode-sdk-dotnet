namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal enum CoreSchemaShape
{
    Unsupported = 0,
    Unrestricted = 1,
    Primitive = 2,
    Enum = 3,
    Array = 4,
    Object = 5,
    Literal = 6,
    Union = 7,
    Null = 8,
    Tuple = 9,
    JsonString = 10,
}
