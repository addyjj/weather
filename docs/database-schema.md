# Weather database schema

Retrieved through DataGrip MCP on 2026-11-14 from `weather@localhost` (Microsoft SQL Server), database `weather`, schema `dbo`.

This snapshot contains object metadata only. `NN` means NOT NULL. No foreign keys were reported.

```text
DeviceData: table
    + columns
        DateUtc: bigint NN
        TempIn: float NN
        HumidityIn: int NN
        BaromRel: float NN
        BaromAbs: float NN
        TempOut: float NN
        BattOut: bit NN
        HumidityOut: int NN
        WindDir: int NN
        WindSpeed: float NN
        WindGust: float NN
        MaxDailyGust: float NN
        HourlyRainRate: float NN
        EventRain: float NN
        DailyRain: float NN
        WeeklyRain: float NN
        MonthlyRain: float NN
        TotalRain: float NN
        SolarRadiation: float NN
        Uv: int NN
        BattCo2: bit NN
        FeelsLike: float NN
        DewPoint: float NN
        FeelsLikeIn: float NN
        DewPointIn: float NN
        Loc: varchar(100) NN
        Date: datetime NN
    + indices
        PK_DeviceData: clustered unique (DateUtc)
    + keys
        PK_DeviceData: PK (DateUtc)
```

```text
__EFMigrationsHistory: table
    + columns
        MigrationId: nvarchar(150) NN
        ProductVersion: nvarchar(32) NN
    + indices
        PK___EFMigrationsHistory: clustered unique (MigrationId)
    + keys
        PK___EFMigrationsHistory: PK (MigrationId)
```

