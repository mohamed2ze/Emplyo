using Emplyo.Application.Common.Interfaces;

namespace Emplyo.Infrastructure.Services;

public class SystemClock : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
