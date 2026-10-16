using Npgsql;
namespace BelemRestaurantes.Web.Data;
public sealed class DbFactory(IConfiguration config) {
 public NpgsqlConnection Open() {var cs=config.GetConnectionString("Postgres");if(string.IsNullOrWhiteSpace(cs))throw new InvalidOperationException("Configure ConnectionStrings__Postgres via secret store");return new NpgsqlConnection(cs);}
}