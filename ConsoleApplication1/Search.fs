module Search

open Types
open System
open NLog

let private logger = LogManager.GetLogger("default")

let private createOutboundLeg (apiResults:Api.SkyScanner.Root) legId = 
    let apiLeg = 
        apiResults.OutboundItineraryLegs
        |> Array.find (fun x-> x.Id = legId)

    { 
        Duration = TimeSpan.FromMinutes(float apiLeg.Duration)
        NumberOfStops = apiLeg.StopsCount
        DepartsAt = apiLeg.DepartureDateTime
    }

let private createInboundLeg (apiResults:Api.SkyScanner.Root) legId = 
    let apiLeg = 
        apiResults.InboundItineraryLegs
        |> Array.find (fun x-> x.Id = legId)

    { 
        Duration = TimeSpan.FromMinutes(float apiLeg.Duration)
        NumberOfStops = apiLeg.StopsCount
        DepartsAt = apiLeg.DepartureDateTime
    }


let private getSearchResultsFromItinerary (apiResults:Api.SkyScanner.Root) (quotes:Map<int, Api.SkyScanner.Quote>) (itinerary:Api.SkyScanner.Itinerary) = 
    let quotes = 
        itinerary.PricingOptions
        |> Array.map (fun x -> x.QuoteIds |> List.ofArray)
        |> Array.fold (fun acc x -> x @ acc) List.Empty
        |> List.map (fun quoteId -> quotes.[quoteId])

    quotes
        |> List.map (fun quote ->
            {
                Outbound =  (createOutboundLeg apiResults) itinerary.OutboundLegId
                Inbound = (createInboundLeg apiResults) itinerary.InboundLegId
                Price = quote.Price
            }
        )

let private isFlightResultSuitable (search:FlightSearch) (searchResult:SearchResult) = 
    searchResult.Inbound.Duration < search.MaxFlightTime 
    && searchResult.Outbound.Duration < search.MaxFlightTime
    && searchResult.Price <= search.MaxPrice
    && searchResult.Inbound.NumberOfStops <= search.MaxStops
    && searchResult.Outbound.NumberOfStops <= search.MaxStops


let GetPotentialFlightDates (search:FlightSearch) = seq {
    for fromDay in 0 .. search.ToDate.Subtract(search.FromDate).Days do
        for toDay in search.NumDaysBetweenFlights do
            yield (search.FromDate.AddDays(float fromDay), search.FromDate.AddDays(float (fromDay + toDay)))
}

let private searchForFlightsOn (search:FlightSearch) (outboundDate:DateTime, inboundDate:DateTime) = async {
    let outboundDateStr = outboundDate.ToShortDateString()
    let inboundDateStr =  inboundDate.ToShortDateString()

    logger.Info(sprintf "Searching for %s - %s .." outboundDateStr inboundDateStr)

    let! results = Api.SearchFlights { OutboundDate = outboundDate; InboundDate = inboundDate; Destination = "BKK" }

    logger.Info(sprintf "%i flights available for %s - %s" (results.Itineraries.Length) outboundDateStr inboundDateStr)

    if results.Itineraries.Length = 0 then 
        return []
    else
        let quotesMap =
            results.Quotes 
            |> Array.map (fun x -> (x.Id, x))
            |> Map.ofArray

        return results.Itineraries
            |> List.ofArray
            |> List.map (getSearchResultsFromItinerary results quotesMap)
            |> List.reduce (fun acc x -> x @ acc)
            |> List.filter (isFlightResultSuitable search)
}



let FindSuitableFlights (search:FlightSearch) = async {
    let potentialDateCombos = GetPotentialFlightDates search |> List.ofSeq
    logger.Info(sprintf "Searching %i date combinations" (potentialDateCombos.Length))

    let searchResultsForAllDateCombos = 
        potentialDateCombos
        |> List.map (fun x -> searchForFlightsOn search x |> Async.RunSynchronously)
    

    let allSearchResults =
        searchResultsForAllDateCombos
        |> List.reduce (fun acc x -> x @ acc)

    logger.Info(sprintf "Returned %i total search results" allSearchResults.Length)

    return allSearchResults
        
        |> List.sortBy (fun x -> x.Price)
        |> Seq.take 10
}
    