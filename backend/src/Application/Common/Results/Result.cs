namespace AiPlatform.Application.Common.Results;

// Expected/business failure exception দিয়ে না, এই Result দিয়ে ফেরত যায় (CLAUDE.md error strategy)।
// Unexpected error-ই শুধু throw হয় → global handler ধরে।

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    Failure
}

public record Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);
    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);
    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);
    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);
}

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        // guard: success হলে error None হতেই হবে; failure হলে হতে পারে না
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException("Success result cannot carry an error.");
        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException("Failure result must carry an error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);
    public static Result<TValue> Failure<TValue>(Error error) => Result<TValue>.Failure(error);

    public static implicit operator Result(Error error) => Failure(error);
}

public class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(TValue value) : base(true, Error.None) => _value = value;
    private Result(Error error) : base(false, error) => _value = default;

    // success না হলে Value পড়া bug — ইচ্ছাকৃত throw
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value of a failed result.");

    public static Result<TValue> Success(TValue value) => new(value);
    public static new Result<TValue> Failure(Error error) => new(error);

    public static implicit operator Result<TValue>(TValue value) => Success(value);
    public static implicit operator Result<TValue>(Error error) => Failure(error);
}
