using CompanioNation.Shared;

namespace CompanioNation.Shared.Tests;

public class CityTests
{
    [Fact]
    public void WhenCityPropertiesSetThenTheyAreRetained()
    {
        var city = new City
        {
            Geonameid = 6167865,
            ContinentCode = "NA",
            CountryCode = "CA",
            CountryName = "Canada",
            Admin1Name = "Ontario",
            CityName = "Toronto"
        };

        Assert.Equal(6167865, city.Geonameid);
        Assert.Equal("NA", city.ContinentCode);
        Assert.Equal("CA", city.CountryCode);
        Assert.Equal("Canada", city.CountryName);
        Assert.Equal("Ontario", city.Admin1Name);
        Assert.Equal("Toronto", city.CityName);
    }

    [Fact]
    public void WhenNearestCityLookupFailsThenResponseHasNoData()
    {
        // Mirrors the contract GetNearestCities relies on: a failed lookup
        // returns a non-success wrapper with null data and the supplied code.
        var result = ResponseWrapper<City>.Fail(50002, "City not found.");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
        Assert.Equal(50002, result.ErrorCode);
    }
}
