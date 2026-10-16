using System.Data;
using Dapper;

namespace BelemRestaurantes.Infrastructure.MySql;

internal sealed class UtcDateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public override DateTimeOffset Parse(object value) => value switch
    {
        DateTimeOffset instant => instant,
        DateTime date => new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)),
        _ => throw new DataException("Instante MySQL inválido.")
    };

    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
    {
        parameter.DbType = DbType.DateTime;
        parameter.Value = value.UtcDateTime;
    }
}
