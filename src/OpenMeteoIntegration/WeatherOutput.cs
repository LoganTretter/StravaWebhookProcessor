namespace OpenMeteoIntegration;

public class WeatherOutput
{
    public List<WeatherOutputDetail> OutputDetails { get; set; } = [];
}

public class WeatherOutputDetail
{
    public DateTimeOffset Time { get; set; }
    public decimal TemperatureFahrenheit { get; set; }
    public decimal DewPointFahrenheit { get; set; }
    public decimal RelativeHumidityPercent { get; set; }
    public decimal PrecipitationInches { get; set; }
    public WMO_Code WeatherCode { get; set; }
    public decimal WindSpeedMph { get; set; }
    public decimal WindDirectionDegrees { get; set; }
    public WindDirection WindDirection => GetWindDirection(WindDirectionDegrees);
    public decimal WindGustSpeedMph { get; set; }

    public static WindDirection GetWindDirection(decimal windDirectionDegrees)
    {
        var degreesPerDir = 22.5m;

        if (windDirectionDegrees < (degreesPerDir / 2))
            return WindDirection.N;
        if (windDirectionDegrees < (degreesPerDir * 3 / 2))
            return WindDirection.NNE;
        if (windDirectionDegrees < (degreesPerDir * 5 / 2))
            return WindDirection.NE;
        if (windDirectionDegrees < (degreesPerDir * 7 / 2))
            return WindDirection.ENE;
        if (windDirectionDegrees < (degreesPerDir * 9 / 2))
            return WindDirection.E;
        if (windDirectionDegrees < (degreesPerDir * 11 / 2))
            return WindDirection.ESE;
        if (windDirectionDegrees < (degreesPerDir * 13 / 2))
            return WindDirection.SE;
        if (windDirectionDegrees < (degreesPerDir * 15 / 2))
            return WindDirection.SSE;
        if (windDirectionDegrees < (degreesPerDir * 17 / 2))
            return WindDirection.S;
        if (windDirectionDegrees < (degreesPerDir * 19 / 2))
            return WindDirection.SSW;
        if (windDirectionDegrees < (degreesPerDir * 21 / 2))
            return WindDirection.SW;
        if (windDirectionDegrees < (degreesPerDir * 23 / 2))
            return WindDirection.WSW;
        if (windDirectionDegrees < (degreesPerDir * 25 / 2))
            return WindDirection.W;
        if (windDirectionDegrees < (degreesPerDir * 27 / 2))
            return WindDirection.WNW;
        if (windDirectionDegrees < (degreesPerDir * 29 / 2))
            return WindDirection.NW;
        if (windDirectionDegrees < (degreesPerDir * 31 / 2))
            return WindDirection.NNW;
        
        return WindDirection.N;
    }
}

public enum WindDirection
{
    N,
    NNE,
    NE,
    ENE,
    E,
    ESE,
    SE,
    SSE,
    S,
    SSW,
    SW,
    WSW,
    W,
    WNW,
    NW,
    NNW
}

public enum WMO_Code
{
    Clear = 0,
    MainlyClear = 1,
    PartlyCloudy = 2,
    Overcast = 3,
    Fog = 45,
    DepositingRimeFog = 48,
    LightDrizzle = 51,
    ModerateDrizzle = 53,
    DenseDrizzle = 55,
    LightFreezingDrizzle = 56,
    DenseFreezingDrizzle = 57,
    SlightRain = 61,
    ModerateRain = 63,
    HeavyRain = 65,
    LightFreezingRain = 66,
    HeavyFreezingRain = 67,
    SlightSnowFall = 71,
    ModerateSnowFall = 73,
    HeavySnowFall = 75,
    SnowGrains = 77,
    SlightRainShowers = 80,
    ModerateRainShowers = 81,
    ViolentRainShowers = 82,
    SlightSnowShowers = 85,
    HeavySnowShowers = 86,
    Thunderstorms = 95,
    ThunderstormsWithSlightHail = 96,
    ThunderstormsWithHeavyHail = 99
}