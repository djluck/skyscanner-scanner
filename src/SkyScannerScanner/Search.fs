﻿module Search

open Types
open System
open NLog
open SkyScannerApi

let private idealOutboundDays = lazy ([DayOfWeek.Wednesday; DayOfWeek.Thursday; DayOfWeek.Friday; DayOfWeek.Saturday] |> Set.ofList)
let private idealInboundDays = lazy ([DayOfWeek.Saturday; DayOfWeek.Sunday; DayOfWeek.Monday; DayOfWeek.Tuesday] |> Set.ofList)
let private idealNumberOfDaysToTravel = [14 .. 18]
let private isIdealOutboundDay (outboundDate:DateTime) = idealOutboundDays.Value.Contains(outboundDate.DayOfWeek)
let private isIdealInboundDay (inboundDate:DateTime) = idealInboundDays.Value.Contains(inboundDate.DayOfWeek)

let GetIdealFlightDates (fromDate:DateTime, toDate:DateTime) = seq {
    for fromDay in 0 .. toDate.Subtract(fromDate).Days do 
        let outboundDate = fromDate.AddDays(float fromDay)
        if idealOutboundDays.Value.Contains(outboundDate.DayOfWeek) then 
            for toDay in idealNumberOfDaysToTravel do 
                let inboundDate = fromDate.AddDays(float (fromDay + toDay))
                if idealInboundDays.Value.Contains(inboundDate.DayOfWeek) then
                    yield (outboundDate, inboundDate)
}

let private logger = LogManager.GetLogger("Search")

let private isFlightResultSuitable (search:FlightSearch) (searchResult:SearchResult) = 
    searchResult.Inbound.Duration < search.MaxFlightTime 
    && searchResult.Outbound.Duration < search.MaxFlightTime
    && searchResult.Price <= search.MaxPrice
    && searchResult.Inbound.NumberOfStops <= search.MaxStops
    && searchResult.Outbound.NumberOfStops <= search.MaxStops


let private searchForFlightsOn (search:FlightSearch) apiRequest = async {
    let outboundDateStr = apiRequest.OutboundDate.ToString("ddd dd/MM")
    let inboundDateStr =  apiRequest.InboundDate.ToString("ddd dd/MM")

    logger.Info(sprintf "Searching for flights: %A" apiRequest)

    let cachedResult = MongoCaching.GetSearchResult apiRequest

    match cachedResult with 
    | Some result ->
        return result |> List.filter (isFlightResultSuitable search)
    | None ->
        let! results = SkyScannerApi.SearchFlights(apiRequest)
        match results with
        | Some r ->
            MongoCaching.StoreSearchResult apiRequest results
            return r |> List.filter (isFlightResultSuitable search)
        | None ->
            return []
}

let FindSuitableFlights (search:FlightSearch) = async {
    let dateAndDestinationCombinations = seq {
        for (outboundDate, inboundDate) in search.FlightDatesToSearchBetween do
            for destination in search.Destinations do 
                yield { 
                    OutboundDate = outboundDate
                    InboundDate = inboundDate
                    Destination = destination 
                }
    }

    logger.Info(sprintf "Searching %i date & location combinations" ((dateAndDestinationCombinations) |> Seq.length))

    let! searchResultsForAllDateCombos = 
        dateAndDestinationCombinations
        |> Seq.map (searchForFlightsOn search)
        |> Async.Parallel
    

    let allSearchResults =
        searchResultsForAllDateCombos
        |> List.ofArray
        |> List.reduce (fun acc x -> x @ acc)

    logger.Info(sprintf "Returned %i total search results" allSearchResults.Length)

    return allSearchResults
}
    