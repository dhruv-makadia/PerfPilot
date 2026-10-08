namespace PerfPilot.Core.Common;

/// <summary>Base type for errors that map to a specific HTTP problem response.</summary>
public abstract class PerfPilotException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class NotFoundException(string entity, object key)
    : PerfPilotException($"{entity} '{key}' was not found.");

public sealed class ConflictException(string message) : PerfPilotException(message);

/// <summary>Raised when an outbound call (spec download, login) fails for a reason the user must fix.</summary>
public sealed class ExternalCallException(string message, string errorKind, Exception? inner = null)
    : PerfPilotException(message, inner)
{
    public string ErrorKind { get; } = errorKind;
}

public sealed class ValidationException : PerfPilotException
{
    public ValidationException(string message) : base(message)
    {
        Errors = new Dictionary<string, string[]> { [""] = [message] };
    }

    public ValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = new Dictionary<string, string[]>(errors);
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>Collects field errors and throws a single <see cref="ValidationException"/>.</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new();

    public bool HasErrors => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            list = [];
            _errors[field] = list;
        }
        list.Add(message);
    }

    public void ThrowIfAny()
    {
        if (HasErrors)
        {
            throw new ValidationException(_errors.ToDictionary(k => k.Key, v => v.Value.ToArray()));
        }
    }
}
