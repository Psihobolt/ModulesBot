namespace Bot.Platform.Api;

public static class ApiErrors
{
    public static IResult Problem(
        int status,
        string code,
        string title,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (errors is not null)
        {
            extensions["errors"] = errors;
        }

        return Results.Problem(statusCode: status, title: title, extensions: extensions);
    }

    public static IResult Validation(IReadOnlyDictionary<string, string[]> errors) =>
        Problem(StatusCodes.Status400BadRequest, "validation_failed", "Validation failed", errors);

    public static IResult Forbidden(string code = "forbidden", string title = "Forbidden") =>
        Problem(StatusCodes.Status403Forbidden, code, title);

    public static IResult NotFound(string code = "not_found", string title = "Not found") =>
        Problem(StatusCodes.Status404NotFound, code, title);
}
