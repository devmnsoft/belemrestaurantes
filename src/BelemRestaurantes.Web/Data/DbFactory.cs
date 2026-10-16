using MySqlConnector;
namespace BelemRestaurantes.Web.Data;
public sealed class DbFactory(IConfiguration config) {
 public MySqlConnection Open(){
 var cs=config.GetConnectionString("MySql");
 if(string.IsNullOrWhiteSpace(cs))throw new InvalidOperationException("Configure ConnectionStrings__MySql securely on the server");
 return new MySqlConnection(cs);
 }
}