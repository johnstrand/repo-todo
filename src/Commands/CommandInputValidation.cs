using RepoTodo.Data;

namespace RepoTodo.Commands;

internal static class CommandInputValidation
{
    public static bool TryParsePositiveId(string value, out int id)
    {
        return int.TryParse(value, out id) && id > 0;
    }

    public static bool TryParseStatus(string value, out TodoItemStatus status)
    {
        return Enum.TryParse(value, ignoreCase: true, out status) && Enum.IsDefined(status);
    }

    public static string? ValidatePagination(int? skip, int? take)
    {
        if (skip is < 0)
        {
            return "--skip must be zero or greater.";
        }

        return take is < 0 ? "--take must be zero or greater." : null;
    }
}
