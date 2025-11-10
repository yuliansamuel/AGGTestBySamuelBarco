# AMSI / AAG Developer Test – Flight Data API

A .NET 8 Web API that automatically downloads, caches, and stores live flight data from the [AviationStack API](https://aviationstack.com/).  
It uses **Redis JSON** for high-performance caching, **MySQL** for persistence, and **background jobs** for periodic ingestion.  
All cache endpoints are **secured via API key authentication**.

---

## Project Overview

This project was built as part of the **AMSI / AAG Developer Test**, implementing all required functionality and several enhancements:

- ✅ **Automated background ingestion** — downloads and stores flight data at regular intervals.  
- ✅ **MySQL integration** — saves raw flight data using stored procedures (`sp_insert_flights_from_json`).  
- ✅ **Redis JSON caching** — keeps fresh copies of the most recent datasets with snapshots and indexes.  
- ✅ **Search endpoints** — query cached data by airline or airport (OR filter logic).  
- ✅ **API key protection** — all cache endpoints require a valid API key via the `X-API-KEY` header.  
- ✅ **Swagger documentation** — easy to test endpoints interactively.  

---

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

```http
X-API-KEY: YOUR_SECRET_KEY

Example Endpoints
1️⃣ Get Cached JSON by Key
GET /api/cache/get/flights:last

2️⃣ Set Custom JSON
POST /api/cache/setjson/customkey
Content-Type: application/json

{
  "test": "Hello Redis!"
}

3️⃣ Search Flights (by IATA)
POST /api/cache/search
Content-Type: application/json
X-API-KEY: YOUR_SECRET_KEY

{
  "airline_iata": "6E",
  "airport_iata": "HBX"
}

4️⃣ List Redis Keys
GET /api/cache/keys?pattern=flights:*

5️⃣ Manually Trigger Data Ingestion
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

1️⃣Fetch from AviationStack API

2️⃣Store JSON snapshot in Redis (flights:last and flights:snapshot)

3️⃣Update version key

4️⃣Push snapshot reference into a daily index list

5️⃣Insert dataset into MySQL

## API Key Security

Implemented via a custom authentication handler:
ApiKeyAuthenticationHandler.cs

Reads key from X-API-KEY header

Validates against value in configuration

Fails with 401 Unauthorized if missing or invalid

## Example Response (Search)
```[
  {
    "flight_date": "2025-11-08",
    "flight_status": "scheduled",
    "airline": { "iata": "6E", "name": "IndiGo" },
    "departure": { "iata": "HBX", "airport": "Hubli" },
    "arrival": { "iata": "BOM", "airport": "Mumbai" }
  }
]
```
## Author

Yulian Samuel Barco Suarez
