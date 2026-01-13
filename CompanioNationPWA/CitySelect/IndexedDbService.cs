using CompanioNationPWA;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;
using System.Text.Json.Serialization;

public class IndexedDbService
{

    public class GeoNamesData
    {
        [JsonPropertyName("geonames")]
        public List<CountryType> geonames { get; set; } = new();
    }

    public class CountryType
    {
        [JsonPropertyName("country_code")]
        public string country_code { get; set; } = string.Empty;
        [JsonPropertyName("country")]
        public string country { get; set; } = string.Empty; // Renamed property and added JsonPropertyName attribute
        [JsonPropertyName("admin1")]
        public List<Admin1Type> admin1 { get; set; } = new();
    }

    public class Admin1Type
    {
        [JsonPropertyName("admin1_code")]
        public string admin1_code { get; set; } = string.Empty;
        [JsonPropertyName("admin1_name")]
        public string admin1_name { get; set; } = string.Empty;
        [JsonPropertyName("cities")]
        public List<CityType> cities { get; set; } = new();
    }

    public class CityType
    {
        [JsonPropertyName("geonameid")]
        public int geonameid { get; set; }
        [JsonPropertyName("city")]
        public string city { get; set; } = string.Empty;
        [JsonPropertyName("latitude")]
        public double latitude { get; set; }
        [JsonPropertyName("longitude")]
        public double longitude { get; set; }
    }


    private readonly CompanioNationSignalRClient _signalRClient;
    private readonly IJSRuntime _jsRuntime;
    private readonly HttpClient _httpClient;
    private static readonly SemaphoreSlim _initializationSemaphore = new SemaphoreSlim(1, 1);
    private static bool _isInitialized = false;

    public IndexedDbService(IJSRuntime jsRuntime, CompanioNationSignalRClient signalRClient, NavigationManager navigationManager)
    {
        _jsRuntime = jsRuntime;
        _signalRClient = signalRClient;
        _httpClient = new HttpClient { BaseAddress = new Uri(navigationManager.BaseUri) };
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializationSemaphore.WaitAsync();
        try
        {
            if (_isInitialized)
            {
                return;
            }

            var storeSchemas = new[]
            {
                new
                {
                    name = "cities",
                    primaryKey = "geonameid",
                    indexes = new[]
                    {
                        new { name = "city", keyPath = "city", unique = false }
                    }
                }
            };

            await _jsRuntime.InvokeVoidAsync("indexedDbHelper.openDb", "GeoNamesDB", 1, storeSchemas);

            var cities = await GetRecordsAsync<CityType>("cities");
            if (cities == null || !cities.Any())
            {
                var initialCities = await LoadCitiesFromJsonAsync("FullGeoNamesJSONDATA.json");

                foreach (var city in initialCities)
                {
                    await AddRecordAsync("cities", city);
                }
            }

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            await _signalRClient.LogError(ex);
        }
        finally
        {
            _initializationSemaphore.Release();
        }
    }

    private async Task<List<CityType>> LoadCitiesFromJsonAsync(string jsonFilePath)
    {
        var json = await _httpClient.GetStringAsync(jsonFilePath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var geoNamesData = JsonSerializer.Deserialize<GeoNamesData>(json, options);
        var cities = new List<CityType>();

        if (geoNamesData?.geonames != null)
        {
            foreach (var country in geoNamesData.geonames)
            {
                foreach (var admin1 in country.admin1)
                {
                    foreach (var city in admin1.cities)
                    {
                        cities.Add(new CityType
                        {
                            geonameid = city.geonameid,
                            city = city.city,
                            latitude = city.latitude,
                            longitude = city.longitude
                        });
                    }
                }
            }
        }

        return cities;
    }


    public async Task<List<CityType>> GetCitiesAsync(string searchTerm)
    {
        var cities = await GetRecordsAsync<CityType>("Cities");
        return cities.Where(c => c.city.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private async Task AddRecordAsync<T>(string storeName, T record)
    {
        await _jsRuntime.InvokeVoidAsync("indexedDbHelper.addRecord", "GeoNamesDB", storeName, record);
    }

    private async Task<List<T>> GetRecordsAsync<T>(string storeName)
    {
        return await _jsRuntime.InvokeAsync<List<T>>("indexedDbHelper.getRecords", "GeoNamesDB", storeName);
    }
}
