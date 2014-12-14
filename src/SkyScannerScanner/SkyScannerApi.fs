module SkyScannerApi

open System
open RestSharp
open System.Net
open FSharp.Data
open Types
open NLog

type SkyScanner = JsonProvider<"skyScanner.json">
type ApiRequest = { 
    OutboundDate : DateTime 
    InboundDate : DateTime
    Destination : string
}

let private client = new RestClient("http://www.skyscanner.net/dataservices/")
let private logger = LogManager.GetLogger("SkyScannerApi")

let private executeAsync request = async {
    let! response = client.ExecuteTaskAsync(request) |> Async.AwaitTask 

    match response.ResponseStatus with
    | ResponseStatus.Completed -> return response
    | _ -> return failwith "Response did not complete succesfully"
}

let private failOnStatusCode (statusCode:HttpStatusCode) = failwith (sprintf "Response had status code of %O" statusCode)


let private createSearchSession (searchRequest:ApiRequest) = async {
    let request = new RestRequest("routedate/v2.0/")

    request.Method <- Method.POST

    request
        .AddHeader("Accept", "application/json, text/javascript, */*; q=0.01")
        .AddQueryParameter("use204", "true")
        .AddQueryParameter("abvariant", "fls_LightRedirect%3Aa")
        .AddParameter("MergeCodeshares", "false", ParameterType.GetOrPost)
        .AddParameter("SkipMixedAirport", "false", ParameterType.GetOrPost)
        .AddParameter("OriginPlace", "LOND", ParameterType.GetOrPost)
        .AddParameter("DestinationPlace", searchRequest.Destination, ParameterType.GetOrPost)
        .AddParameter("OutboundDate", searchRequest.OutboundDate.ToString("yyyy-MM-dd"), ParameterType.GetOrPost)
        .AddParameter("InboundDate", searchRequest.InboundDate.ToString("yyyy-MM-dd"), ParameterType.GetOrPost)
        .AddParameter("Passengers.Adults", "2", ParameterType.GetOrPost)
        .AddParameter("Passengers.Children", "0", ParameterType.GetOrPost)
        .AddParameter("Passengers.Infants", "0", ParameterType.GetOrPost)
        .AddParameter("UserInfo.CountryId", "UK", ParameterType.GetOrPost)
        .AddParameter("UserInfo.LanguageId", "EN", ParameterType.GetOrPost)
        .AddParameter("UserInfo.CurrencyId", "GBP", ParameterType.GetOrPost)
        .AddParameter("CabinClass", "Economy", ParameterType.GetOrPost)
        .AddParameter("UserInfo.ChannelId", "transportfunnel", ParameterType.GetOrPost)
        .AddParameter("JourneyModes", "flight", ParameterType.GetOrPost)
        .AddParameter("RequestId", Guid.NewGuid(), ParameterType.GetOrPost)
        |> ignore

    let! response = executeAsync request

    match response.StatusCode with 
    | HttpStatusCode.Created -> return Some (SkyScanner.Parse(response.Content))
    | _ -> return failOnStatusCode response.StatusCode
}

let private getUpdateOnSearchSession (sessionId:Guid) = async {
    let request = new RestRequest(sprintf "routedate/v2.0/%O" sessionId)

    request
        .AddHeader("Accept", "application/json, text/javascript, */*; q=0.01")
        .AddQueryParameter("use204", "true")
        .AddQueryParameter("abvariant", "fls_LightRedirect%3Aa")
        |> ignore

    let! response = executeAsync request

    match response.StatusCode with 
    | HttpStatusCode.NoContent -> return None
    | HttpStatusCode.NotFound -> return None
    | HttpStatusCode.OK -> return Some (SkyScanner.Parse(response.Content))
    | _ -> return failOnStatusCode response.StatusCode
}

let private haveAllResults (response:SkyScanner.Root) = 
    response.QuoteRequests 
        |> Array.forall (fun x -> not x.HasLiveUpdateInProgress)

let rec private waitForResults (sessionId:Guid) lastResponse triesRemaining = async {
    if triesRemaining = 0 then 
        return lastResponse
    else
        do! Async.Sleep 20000 //don't want to hammer SkyScanner's API...

        let! updatedSearchResults = getUpdateOnSearchSession sessionId

        match updatedSearchResults with 
        | None -> return! waitForResults sessionId lastResponse (triesRemaining - 1)
        | Some response when haveAllResults response -> return response
        | Some response -> return! waitForResults sessionId response (triesRemaining - 1)
}

let private createOutboundLeg (apiResults:SkyScanner.Root) legId = 
    let apiLeg = 
        apiResults.OutboundItineraryLegs
        |> Array.find (fun x-> x.Id = legId)

    { 
        Duration = TimeSpan.FromMinutes(float apiLeg.Duration)
        NumberOfStops = apiLeg.StopsCount
        DepartsAt = apiLeg.DepartureDateTime
    }


let private createInboundLeg (apiResults:SkyScanner.Root) legId = 
    let apiLeg = 
        apiResults.InboundItineraryLegs
        |> Array.find (fun x-> x.Id = legId)

    { 
        Duration = TimeSpan.FromMinutes(float apiLeg.Duration)
        NumberOfStops = apiLeg.StopsCount
        DepartsAt = apiLeg.DepartureDateTime
    }


let private getSearchResultsFromItinerary (request:ApiRequest) (apiResults:SkyScanner.Root) (quotes:Map<int, SkyScanner.Quote>) (itinerary:SkyScanner.Itinerary) = 
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
                Destination = request.Destination
            }
        )


let private transformResults (request:ApiRequest) (results:SkyScanner.Root) =
    if results.Itineraries.Length = 0 then 
        []
    else
        let quotesMap =
            results.Quotes 
            |> Array.map (fun x -> (x.Id, x))
            |> Map.ofArray

        results.Itineraries
            |> List.ofArray
            |> List.map (getSearchResultsFromItinerary request results quotesMap)
            |> List.reduce (fun acc x -> x @ acc)

let private sendRequestAndAwaitResponse request = async {
     let! searchSession = createSearchSession request

    match searchSession with 
    | Some session when haveAllResults session -> return Some (transformResults request session)
    | Some session -> 
        let! rawResults = (waitForResults session.SessionKey session 2)
        return Some (transformResults request rawResults)
    | None ->
        return None 
}

let SearchFlights request = async {
    logger.Info(sprintf "Scanning SkyScanner for %A" request)
    let! response = sendRequestAndAwaitResponse request

    match response with 
    | Some r -> 
        logger.Info(sprintf "Found %i results for %A" r.Length request) |> ignore
    | None ->
        logger.Warn(sprintf "Found no results for %A") |> ignore

    return response
}

//how do we limit requests in paralell 