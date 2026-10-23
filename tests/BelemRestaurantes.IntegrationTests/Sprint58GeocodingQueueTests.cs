using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Infrastructure.Migrations;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BelemRestaurantes.IntegrationTests;

[Collection("MySql")]
public sealed class Sprint58GeocodingQueueTests
{
    private sealed class GeoRow
    {
        public decimal? Latitude { get; set; }
        public string? Precisao { get; set; }
        public string? Source { get; set; }
        public GeoRow() { }
    }

    [Fact] public async Task QueueLifecycleReservesDailyLimitPlacesOnceAndProtectsTier1()
    {
        using var root=(ServiceProvider)ServiceContainer.Build();using var scope=root.CreateScope();var sp=scope.ServiceProvider;
        await sp.GetRequiredService<MigrationRunner>().ApplyAsync(default);
        var queue=sp.GetRequiredService<IGeocodingQueue>();
        await using var db=await sp.GetRequiredService<MySqlConnectionFactory>().OpenAsync(default);
        var token=Guid.NewGuid().ToString("N")[..8];var name="Fixture Geocoding "+token;long id=0,ownerFixture=0;
        try
        {
            // Reserva diária compartilhada: a primeira entra, a segunda respeita o teto relativo ao já usado.
            var before=await db.ExecuteScalarAsync<int>("SELECT COALESCE((SELECT granted FROM geocoding_runs WHERE run_day=UTC_DATE()),0)");
            var limit=Math.Max(before+1,1);
            Assert.True(await queue.TryReserveDailyAsync(limit,default));
            Assert.False(await queue.TryReserveDailyAsync(limit,default));

            id=await db.ExecuteScalarAsync<long>("""
                INSERT INTO restaurants(name,normalized_name,city,normalized_city,address,category,status,created_at,updated_at)
                VALUES(@name,LOWER(@name),'Belém','belem',@address,'Teste','published',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
                SELECT LAST_INSERT_ID()
                """,new{name,address="Travessa Teste "+token+", São Brás"});
            ownerFixture=await db.ExecuteScalarAsync<long>("""
                INSERT INTO restaurants(name,normalized_name,city,normalized_city,address,category,status,created_at,updated_at)
                VALUES(@name,LOWER(@name),'Belém','belem',@address,'Teste','published',UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
                SELECT LAST_INSERT_ID()
                """,new{name="Fixture Geocoding Dono "+token,address="Travessa Do Dito "+token+", São Brás"});
            await db.ExecuteAsync("INSERT INTO restaurant_owners(restaurant_id,user_id,role,created_at) VALUES(@id,@user,'owner',UTC_TIMESTAMP(6))",new{id=ownerFixture,user="fixture-geo-owner-"+token});

            // Esgota o enqueue em lotes (ORDER BY id): a ficha nova fica por último e a de dono nunca entra.
            var drained=0;
            while(drained<200000){var added=await queue.EnqueueEligibleAsync(50,default);if(added==0)break;drained+=added;}
            Assert.Equal(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM geocoding_tasks WHERE restaurant_id=@id",new{id}));
            Assert.Equal("pending",await db.ExecuteScalarAsync<string>("SELECT state FROM geocoding_tasks WHERE restaurant_id=@id",new{id}));
            Assert.Equal(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM geocoding_tasks WHERE restaurant_id=@id",new{id=ownerFixture}));
            Assert.Equal(0,await queue.EnqueueEligibleAsync(50,default)); // fila esgotada: nada novo entra

            Assert.True(await queue.ApplyCoordinatesAsync(id,-1.4667324,-48.4918527,"approximate",99001,default));
            var placed=await db.QueryFirstAsync<GeoRow>("SELECT latitude AS Latitude,coordinate_precision AS Precisao,coordinate_source AS Source FROM restaurants WHERE id=@id",new{id});
            Assert.NotNull(placed.Latitude);
            Assert.Equal("approximate",placed.Precisao);
            Assert.Contains("Nominatim",placed.Source);
            Assert.False(await queue.ApplyCoordinatesAsync(id,-1.4,-48.5,"rooftop",1,default)); // já tem coordenada: write-once
            await queue.FinishAsync(id,true,"approximate",0,"approximate",99001);
            Assert.Equal("completed",await db.ExecuteScalarAsync<string>("SELECT state FROM geocoding_tasks WHERE restaurant_id=@id",new{id}));

            // Protecao Tier 1: com vinculo de dono, nenhuma coordenada externa entra.
            await db.ExecuteAsync("UPDATE restaurants SET latitude=NULL,longitude=NULL,coordinate_precision=NULL,coordinate_source=NULL WHERE id=@id",new{id});
            await db.ExecuteAsync("UPDATE geocoding_tasks SET state='pending',next_run_at=UTC_TIMESTAMP(6),attempts=0,last_status=NULL,result_precision=NULL,osm_place_id=NULL WHERE restaurant_id=@id",new{id});
            await db.ExecuteAsync("INSERT INTO restaurant_owners(restaurant_id,user_id,role,created_at) VALUES(@id,@user,'owner',UTC_TIMESTAMP(6))",new{id,user="fixture-geo-"+token});
            Assert.False(await queue.ApplyCoordinatesAsync(id,-1.4,-48.5,"rooftop",1,default));
            await queue.FinishAsync(id,true,"skipped",0);
        }
        finally
        {
            if(id!=0)
            {
                await db.ExecuteAsync("DELETE FROM restaurant_owners WHERE restaurant_id=@id",new{id});
                await db.ExecuteAsync("DELETE FROM geocoding_tasks WHERE restaurant_id=@id",new{id});
                await db.ExecuteAsync("DELETE FROM restaurants WHERE id=@id",new{id});
            }
            if(ownerFixture!=0)
            {
                await db.ExecuteAsync("DELETE FROM restaurant_owners WHERE restaurant_id=@id",new{id=ownerFixture});
                await db.ExecuteAsync("DELETE FROM geocoding_tasks WHERE restaurant_id=@id",new{id=ownerFixture});
                await db.ExecuteAsync("DELETE FROM restaurants WHERE id=@id",new{id=ownerFixture});
            }
        }
    }
}
