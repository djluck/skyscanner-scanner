module Api

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

type ISkyScannerApi = 
    abstract SearchFlights : (ApiRequest) -> Async<List<SearchResult>>


type SkyScannerApi () =
    let client = new RestClient("http://www.skyscanner.net/dataservices/")
    let logger = LogManager.GetLogger("default")

    let executeAsync request = async {
        let! response = client.ExecuteTaskAsync(request) |> Async.AwaitTask 

        match response.ResponseStatus with
        | ResponseStatus.Completed -> return response
        | _ -> return failwith "Response did not complete succesfull"
    }

    let failOnStatusCode (statusCode:HttpStatusCode) = failwith (sprintf "Response had status code of %O" statusCode)


    let createSearchSession (searchRequest:ApiRequest) = async {
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
        | HttpStatusCode.Created -> return SkyScanner.Parse(response.Content)
        | _ -> return failOnStatusCode response.StatusCode
    }

    let getUpdateOnSearchSession (sessionId:Guid) = async {
        let request = new RestRequest(sprintf "routedate/v2.0/%O" sessionId)

        request
            .AddHeader("Accept", "application/json, text/javascript, */*; q=0.01")
            .AddQueryParameter("use204", "true")
            .AddQueryParameter("abvariant", "fls_LightRedirect%3Aa")
            |> ignore

        let! response = executeAsync request

       match response.StatusCode with 
        | HttpStatusCode.NoContent -> return None
        | HttpStatusCode.OK -> return Some (SkyScanner.Parse(response.Content))
        | _ -> return failOnStatusCode response.StatusCode
    }

    let haveAllResults (response:SkyScanner.Root) = 
        response.QuoteRequests 
        |> Array.forall (fun x -> not x.HasLiveUpdateInProgress)

    let rec waitForResults (sessionId:Guid) lastResponse triesRemaining = async {
        if triesRemaining = 0 then 
            return lastResponse
        else
            do! Async.Sleep 10000 //don't want to hammer SkyScanner's API...

            let! updatedSearchResults = getUpdateOnSearchSession sessionId

            match updatedSearchResults with 
            | None -> return! waitForResults sessionId lastResponse (triesRemaining - 1)
            | Some response when haveAllResults response -> return response
            | Some response -> return! waitForResults sessionId response (triesRemaining - 1)
    }

    let createOutboundLeg (apiResults:SkyScanner.Root) legId = 
        let apiLeg = 
            apiResults.OutboundItineraryLegs
            |> Array.find (fun x-> x.Id = legId)

        { 
            Duration = TimeSpan.FromMinutes(float apiLeg.Duration)
            NumberOfStops = apiLeg.StopsCount
            DepartsAt = apiLeg.DepartureDateTime
        }


    let createInboundLeg (apiResults:SkyScanner.Root) legId = 
        let apiLeg = 
            apiResults.InboundItineraryLegs
            |> Array.find (fun x-> x.Id = legId)

        { 
            Duration = TimeSpan.FromMinutes(float apiLeg.Duration)
            NumberOfStops = apiLeg.StopsCount
            DepartsAt = apiLeg.DepartureDateTime
        }


    let getSearchResultsFromItinerary (apiResults:SkyScanner.Root) (quotes:Map<int, SkyScanner.Quote>) (itinerary:SkyScanner.Itinerary) = 
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


    let transformResults (results:SkyScanner.Root) =
        if results.Itineraries.Length = 0 then 
            []
        else
            let quotesMap =
                results.Quotes 
                |> Array.map (fun x -> (x.Id, x))
                |> Map.ofArray

            results.Itineraries
                |> List.ofArray
                |> List.map (getSearchResultsFromItinerary results quotesMap)
                |> List.reduce (fun acc x -> x @ acc)


    interface ISkyScannerApi with
        member this.SearchFlights request = async {
            let! searchSession = createSearchSession request

            if haveAllResults searchSession then
                return transformResults searchSession
            else
                let! rawResults = (waitForResults searchSession.SessionKey searchSession 1)
                return transformResults rawResults
        }


type CachingSkyScannerApi (api:ISkyScannerApi) =
    interface ISkyScannerApi with
        member this.SearchFlights request = async {
            
            let! results = api.SearchFlights(request)
            return results
        }