module Types

open System

type FlightSearch = { 
    FromDate: DateTime
    ToDate: DateTime
    NumDaysBetweenFlights : List<int>
    MaxFlightTime : TimeSpan
    MaxStops : int
    MaxPrice: Decimal
    Destinations: List<string>
}

type Leg = {
    Duration: TimeSpan
    NumberOfStops: int
    DepartsAt : DateTime
}

type SearchResult = {
    Inbound: Leg
    Outbound: Leg
    Price: Decimal
}
