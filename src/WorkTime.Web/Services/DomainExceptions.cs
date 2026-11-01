namespace WorkTime.Web.Services;

/// <summary>Ожидаемая ошибка бизнес-логики. Сообщение показывается пользователю.</summary>
public abstract class DomainException(string message) : Exception(message);

public sealed class InvalidInputException(string message) : DomainException(message);

public sealed class NotFoundException(string message) : DomainException(message);

public sealed class ConflictException(string message) : DomainException(message);
