﻿using AirportExpoler.Helpers;
using CsvHelper;
using CsvHelper.Configuration;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using GoogleApi;
using GoogleApi.Entities.Common.Enums;
using GoogleApi.Entities.Places.Details.Request;
using GoogleApi.Entities.Places.Details.Request.Enums;
using GoogleApi.Entities.Places.Photos.Request;
using GoogleApi.Entities.Places.Search.NearBy.Request;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AirportExpoler.Pages
{
    public class IndexModel : PageModel
    {
        private IHostingEnvironment _hostingEnvironment;

        //this property is used to remove access token from index/page
        public string MapboxAccessToken { get; }

        public string GoogleApiKey { get; }
        
        public IndexModel(IConfiguration configuration,
            IHostingEnvironment hostingEnvironment)
        {
            _hostingEnvironment = hostingEnvironment;
            MapboxAccessToken = configuration["Mapbox:AccessToken"];
            GoogleApiKey = configuration["Google:ApiKey"];
        }

        public void OnGet()
        {

        }

        //this is the Handler <?handler=airports> to read from CSV and add dots to the map with GeoJson
        public IActionResult OnGetAirports()
        {
            //if it has bad data, ignore them
            var configuration = new Configuration
            {
                BadDataFound = context => { }
            };

            //read the file from folder
            using (var sr = new StreamReader(Path.Combine(_hostingEnvironment.WebRootPath, "airports.dat")))
            using (var reader = new CsvReader(sr, configuration))
            {
                //it is used to get the GeoJson result in a list 
                FeatureCollection featureCollection = new FeatureCollection();
                reader.Configuration.TypeConverterOptionsCache.GetOptions(typeof(double)).CultureInfo = CultureInfo.CreateSpecificCulture("en-US");

                //this is needed 
                reader.Configuration.Delimiter = ",";

                while (reader.Read())
                {
                    string name = reader.GetField<string>(1);
                    string iataCode = reader.GetField<string>(4);
                    double latitude = reader.GetField<double>(6);
                    double longitude = reader.GetField<double>(7);

                    featureCollection.Features.Add(new Feature(
                    new Point(new Position(latitude, longitude)),
                    new Dictionary<string, object>
                    {
                        {"name", name},
                        {"iataCode", iataCode}
                    }));
                }

                return new JsonResult(featureCollection);
            }
        }

        public async Task<IActionResult> OnGetAirportDetail(string name, double latitude, double longitude)
        {
            var airportDetail = new AirportDetail();

            // Execute the search request
            var searchResponse = await GooglePlaces.NearBySearch.QueryAsync(new PlacesNearBySearchRequest
            {
                Key = GoogleApiKey,
                Name = name,
                Location = new Location(latitude, longitude),
                Radius = 500
            });

            // If we did not get a good response, or the list of results are empty then get out of here
            if (!searchResponse.Status.HasValue || searchResponse.Status.Value != Status.Ok || !searchResponse.Results.Any())
                return new BadRequestResult();

            // Get the first result
            var nearbyResult = searchResponse.Results.FirstOrDefault();
            string placeId = nearbyResult.PlaceId;
            string photoReference = nearbyResult.Photos?.FirstOrDefault()?.PhotoReference;
            string photoCredit = nearbyResult.Photos?.FirstOrDefault()?.HtmlAttributions.FirstOrDefault();

            // Execute the details request
            var detailsResponse = await GooglePlaces.Details.QueryAsync(new PlacesDetailsRequest
            {
                Key = GoogleApiKey,
                PlaceId = placeId,
                Fields = FieldTypes.Formatted_Address | 
                FieldTypes.International_Phone_Number | 
                FieldTypes.Website
            });

            // If we did not get a good response then get out of here
            if (!detailsResponse.Status.HasValue || detailsResponse.Status.Value != Status.Ok)
                return new BadRequestResult();

            // Set the details
            var detailsResult = detailsResponse.Result;
            airportDetail.FormattedAddress = detailsResult.FormattedAddress;
            airportDetail.PhoneNumber = detailsResult.InternationalPhoneNumber;
            airportDetail.Website = detailsResult.Website;

            if (photoReference != null)
            {
                // Execute the photo request
                var photosResponse = await GooglePlaces.Photos.QueryAsync(new PlacesPhotosRequest
                {
                    Key = GoogleApiKey,
                    PhotoReference = photoReference,
                    MaxWidth = 400
                });
                if (photosResponse.Buffer != null)
                {
                    airportDetail.Photo = Convert.ToBase64String(photosResponse.Buffer);
                    airportDetail.PhotoCredit = photoCredit;
                }
            }

            return new JsonResult(airportDetail);
        }
    }
}

