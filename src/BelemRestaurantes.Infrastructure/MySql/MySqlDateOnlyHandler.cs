using System.Data;
using System.Globalization;
using Dapper;

namespace BelemRestaurantes.Infrastructure.MySql;

internal sealed class MySqlDateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType=DbType.Date;
        parameter.Value=value.ToDateTime(TimeOnly.MinValue);
    }
    public override DateOnly Parse(object value)=>value switch
    {
        DateOnly date=>date,
        DateTime date=>DateOnly.FromDateTime(date),
        string date=>DateOnly.ParseExact(date,"yyyy-MM-dd",CultureInfo.InvariantCulture),
        _=>throw new DataException("O banco retornou uma data sem formato válido.")
    };
}
