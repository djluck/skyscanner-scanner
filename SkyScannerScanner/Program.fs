open FSharp.Data
open Types
open Search
open System


[<EntryPoint>]
let main argv = 

    let request = { 
        FlightDatesToSearchBetween = Search.GetIdealFlightDates(new DateTime(2015, 1, 20), new DateTime(2015, 3, 21)) |> List.ofSeq
        MaxFlightTime = new TimeSpan(22, 0, 0)
        MaxStops = 2
        MaxPrice = 600m
        Destinations = ["HAN"; "SGN"]
    }
    let result = Search.FindSuitableFlights request |> Async.RunSynchronously
    printfn "Ordered by cost: %A" (result |> List.sortBy (fun x -> x.Price))
    printfn "Ordered by total flight time: %A" (result |> List.sortBy (fun x -> x.Inbound.Duration + x.Outbound.Duration))
    0 // return an integer exit code
