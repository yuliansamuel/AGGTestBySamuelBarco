# AMSI / AAG Developer Test – Flight Data API

A .NET 8 Web API that automatically downloads, caches, and stores live flight data from the AviationStack API (https://aviationstack.com/).  
It uses **Redis JSON** for high-performance caching, **MySQL** for persistence, and **background jobs** for periodic ingestion.  
All cache endpoints are **secured via API key authentication**.

---

## Project Overview

This project was built as part of the **AMSI / AAG Developer Test**, implementing all required functionality:

-  **Automated background ingestion** - downloads and stores flight data at regular intervals.  
-  **MySQL integration** - saves raw flight data using stored procedures (`sp_insert_flights_from_json`).  
-  **Redis JSON caching** - keeps fresh copies of the most recent datasets with snapshots and indexes.  
-  **Search endpoints** - query cached data by airline or airport (OR filter logic).  
-  **API key protection** - all cache endpoints require a valid API key via the `X-API-KEY` header.  
-  **Swagger documentation** - easy to test endpoints interactively.  

---

## Testing endpoints in Swagger

To test the endpoints, clone this repository, open it using Visual Studio, and at the top, you will see the start button, which will run Swagger to test the endpoints.
Once executed, a console will open to track the logs (in a production environment, they would be recorded in the AWS console or in a database) and Swagger to test each piece of the system.
You must use the API key provided in the email.
At Swagger, you will find different endpoints such as:


## Cache
```
api/Cache/get/{key}
api/Cache/setjson/{key}
api/Cache/search
api/Cache/keys
```
## Flights
```
api/Flights/GetFlights
```
 
## api/Cache/keys
In this API, you can list all the keys stored in the Redis cache layer

## api/Cache/search
For using this API, I recommend first running *api/Cache/keys* to list the existing keys in Redis, then using *api/Cache/get/{key}* where the key will be the last available snapshot or *flights:last* by default to view the JSON inside, then construct the body JSON using the *airport IATA* and *airline IATA* to filter and get results, for example 
```json
{
"airline_iata": "MU",
"airport_iata": "PVG"
}
```
## api/Cache/setjson/{key}
Through the header, you can send a key registered in Redis (previously obtained in api/Cache/keys) or a new one, and in the body, what you want to save within that key

## api/Cache/get/{key}
This endpoint receives the registered key in Redis that you want to query through the header. A good example could be flights:last

## api/Flights/GetFlights
This endpoint is used to retrieve information from the AviationStack API (https://aviationstack.com/), which we are using to cache, filter, and store in a database.

---

## Tech Stack

| Component | Technology |
|------------|-------------|
| Language | C# (.NET 8) |
| Framework | ASP.NET Core Web API |
| Database | MySQL (with stored procedures) |
| Cache | Redis (with RedisJSON module) |
| Background Processing | Hosted Service (`BackgroundService`) |
| Auth | Custom API Key via `X-API-KEY` |
| Docs | Swagger / OpenAPI |

---

## Main Components

| File | Purpose |
|------|----------|
| **FlightsJob.cs** | Runs automatically at scheduled intervals to download and cache flight data. |
| **Flights.cs** | Fetches data from the external API and inserts it into the database. |
| **CacheService.cs** | Handles Redis JSON reads, writes, and search filtering (airline/airport). |
| **CacheController.cs** | API endpoints for reading, writing, and searching cache. |
| **FlightsController.cs** | Manual trigger for flight ingestion (for debugging). |
| **ApiKeyAuthenticationHandler.cs** | Middleware that validates API key authentication. |

---

## Authentication

All cache endpoints are protected by an API key.  
Include the following header in your requests:

```
X-API-KEY: YOUR_SECRET_KEY

Example Endpoints
- Get Cached JSON by Key
GET /api/cache/get/flights:last

- Set Custom JSON
POST /api/cache/setjson/customkey
Content-Type: application/json

{
  "test": "Hello Redis!"
}

- Search Flights (by IATA)
POST /api/cache/search
Content-Type: application/json
X-API-KEY: YOUR_SECRET_KEY

{
  "airline_iata": "6E",
  "airport_iata": "HBX"
}

- List Redis Keys
GET /api/cache/keys?pattern=flights:*

- Manually Trigger Data Ingestion
GET /api/flights/getflights
```
## Search Logic

The /api/cache/search endpoint supports filtering by:

airline_iata

airport_iata

If both are provided → OR logic
If only one → filter by that field
If none → return full dataset.

## Background Job

The ingestion job (FlightsJob) runs automatically every Ingestion:IntervalMinutes, performing:

- Fetch from AviationStack API

- Store JSON snapshot in Redis (flights:last and flights:snapshot)

- Update version key

- Push snapshot reference into a daily index list

- Insert dataset into MySQL

## API Key Security

Implemented via a custom authentication handler:
ApiKeyAuthenticationHandler.cs

Reads key from X-API-KEY header

Validates against value in configuration

Fails with 401 Unauthorized if missing or invalid

## Author

Yulian Samuel Barco Suarez
