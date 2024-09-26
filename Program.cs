using System.Text;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;

var config = new ConfigurationBuilder()
           .SetBasePath(Directory.GetCurrentDirectory())
           .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
           .Build();

var uatBaseAddress = "https://vuhl-uat.redchimney.com/apps/moapricingexceptions/PricingExceptions/loan/";
//var prodBaseAddress = "https://vuhl.redchimney.com/apps/moapricingexceptions/PricingExceptions/loan/";

var currentBinDirectory = Directory.GetCurrentDirectory();
var projectRootPath = Directory.GetParent(currentBinDirectory)?.Parent?.Parent?.FullName;
var finalProdList = Path.Combine(projectRootPath, "loans_with_closing_date.csv");
var failedLoansFilePath = Path.Combine(projectRootPath, "failed-loans.txt");
var processedLoansFilePath = Path.Combine(projectRootPath, "processed-loans.txt");

var client = new HttpClient
{
    BaseAddress = new Uri(uatBaseAddress)
};

await RunScriptAsync();

async Task RunScriptAsync()
{
    Console.WriteLine("Starting true-up script\n");
    var processedLoans = LoadProcessedLoanGuidsFromFile(); //This is a seperate file that gets written to in order to keep track of processed loans, to avoid more than one true-up per loan
    var guidList = ParseCsv(finalProdList).Except(processedLoans).ToList();
    var firstFiveGuids = guidList.Take(5).ToList(); // This is just a temporary measure to isolate the first 5 loans in the list

    Console.WriteLine($"Found a total of {guidList.Count} unprocessed loans in the file {finalProdList}\n");
    var failedLoans = new List<string>();

    foreach (var guid in firstFiveGuids)
    {
        var result = await InvokeTrueUpEndpoint(guid.ToString());
        if (result != null)
        {
            failedLoans.Add(result);
        }
        else
        {
            WriteProcessedLoansToFile(guid);
        }
    }

    if (failedLoans.Count > 0)
    {
        Console.WriteLine($"\nFailed to true-up the following loans: {string.Join(", ", failedLoans)}");
        WriteFailedLoanGuidsToFile(failedLoansFilePath, failedLoans);
        Console.WriteLine($"\nFailed loan GUIDs written to {failedLoansFilePath}");
    }
    else
    {
        Console.WriteLine("All loans were successfully processed.");
    }
}

List<Guid> ParseCsv(string filePath)
{
    var data = new List<Guid>();
    using (var reader = new StreamReader(filePath))
    {
        var headerLine = reader.ReadLine();
        if (headerLine == null)
        {
            throw new InvalidOperationException("CSV file is empty or cannot be read.");
        }

        var headers = headerLine.Split(',');
        var loanGuidColumnIndex = Array.IndexOf(headers, "LoanGuid");
        if (loanGuidColumnIndex == -1)
        {
            throw new InvalidOperationException("LoanGuid column not found in the CSV file.");
        }

        var line = string.Empty;
        while ((line = reader.ReadLine()) != null)
        {
            var fields = line.Split(',');
            if (fields.Length > loanGuidColumnIndex)
            {
                if (Guid.TryParse(fields[loanGuidColumnIndex], out Guid loanGuid))
                {
                    data.Add(loanGuid);
                }
                else
                {
                    Console.WriteLine($"Warning: Invalid GUID format in line: {line}");
                }
            }
            else
            {
                Console.WriteLine($"Warning: Line does not contain enough fields: {line}");
            }
        }
    }
    return data;
}
async Task<string?> InvokeTrueUpEndpoint(string loanGuid)
{
    try
    {
        var content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{loanGuid}/trueup", content);

        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Successful true-up for loan {loanGuid}");
            Console.Out.Flush();
            return null;
        }
        else
        {
            Console.WriteLine($"Failed to true-up loan {loanGuid}. Status: {response.StatusCode}");
            Console.Out.Flush();
            return loanGuid;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error processing loan {loanGuid}: {ex.Message}");
        Console.Out.Flush();
        return loanGuid;
    }
}
void WriteFailedLoanGuidsToFile(string filePath, List<string> failedLoans)
{
    using (var writer = new StreamWriter(filePath))
    {
        foreach (var loanGuid in failedLoans)
        {
            writer.WriteLine(loanGuid);
        }
    }
}

void WriteProcessedLoansToFile(Guid loanGuid)
{
    using (var writer = new StreamWriter(processedLoansFilePath, true))
    {
        writer.WriteLine(loanGuid);
    }
}

List<Guid> LoadProcessedLoanGuidsFromFile()
{
    if (!File.Exists(processedLoansFilePath))
        return new List<Guid>();

    return File.ReadAllLines(processedLoansFilePath)
        .Select(line => Guid.Parse(line))
        .ToList();
}

#region This section deals with querying the E360Extract database for loan data and writing it to a CSV file
//var e360ConnectionString = config["ConnectionStrings:E60Extract"];
//var outputFilePath = Path.Combine(desktopPath, "loans_with_closing_date.csv");
////var updatedLoanList = "C:\\Users\\mike.hall\\OneDrive - Veterans United Home Loans\\Desktop\\updated-loan-list.csv";

//string QueryLoanFolder(string loanGuid)
//{
//    using var connection = new SqlConnection(e360ConnectionString);
//    var paramters = new { loanGuid = $"{{{loanGuid}}}" };
//    var query = "SELECT LOAN_LOANFOLDER FROM e360.Encompass_loanData1 WHERE GUID=@loanGuid";
//    var result = connection.Query<string>(query, paramters).FirstOrDefault();

//    return result ?? string.Empty;
//}

//string QueryClosingDate(string loanGuid)
//{
//    using var connection = new SqlConnection(e360ConnectionString);
//    var paramters = new { loanGuid = $"{{{loanGuid}}}" };
//    var query = "SELECT CLOSING_DATE_748 FROM e360.Encompass_loanData1 WHERE GUID=@loanGuid";
//    var result = connection.Query<string>(query, paramters).FirstOrDefault();

//    return result ?? string.Empty;
//}


//List<LoanData> ParseCsv(string filePath)
//{
//    var data = new List<LoanData>();

//    using (var reader = new StreamReader(filePath))
//    {
//        var headerLine = reader.ReadLine();
//        if (headerLine == null)
//        {
//            throw new InvalidOperationException("CSV file is empty or cannot be read.");
//        }

//        var headers = headerLine.Split(',');
//        var loanNumberColumnIndex = Array.IndexOf(headers, "LoanNumber");
//        var loanGuidColumnIndex = Array.IndexOf(headers, "LoanGuid");
//        var loanFolderColumnIndex = Array.IndexOf(headers, "LoanFolder");

//        if (loanGuidColumnIndex == -1)
//        {
//            throw new InvalidOperationException("LoanGuid column not found in the CSV file.");
//        }

//        var line = string.Empty;
//        while ((line = reader.ReadLine()) != null)
//        {
//            var fields = line.Split(',');
//            if (fields.Length > loanGuidColumnIndex)
//            {

//                if (Guid.TryParse(fields[loanGuidColumnIndex], out Guid loanGuid))
//                {
//                    var loanData = new LoanData
//                    {
//                        LoanNumber = fields[loanNumberColumnIndex],
//                        LoanGuid = loanGuid,
//                        LoanFolder = fields[loanFolderColumnIndex]
//                    };
//                    data.Add(loanData);
//                }
//                else
//                {
//                    Console.WriteLine($"Warning: Invalid GUID format in line: {line}");
//                }
//            }
//            else
//            {
//                Console.WriteLine($"Warning: Line does not contain enough fields: {line}");
//            }
//        }
//    }
//    return data;
//}


////void WriteCsvWithLoanFolder(string inputFilePath, string outputFilePath)
////{
////    var loans = ParseCsv(inputFilePath);
////    Console.WriteLine($"Found a total of {loans.Count} in file{inputFilePath}\n");

////    using (var writer = new StreamWriter(outputFilePath))
////    {
////        writer.WriteLine("Loan_Number,LoanGuid,LoanFolder");
////        var count = 0;

////        foreach (var loan in loans)
////        {
////            count++;
////            Console.WriteLine($"Querying loan {count} of total {loans.Count}");
////            // Get the LoanFolder by calling the QueryLoanFolder method
////            string loanFolder = QueryLoanFolder(loan.LoanGuid.ToString());

////            writer.WriteLine($"{loan.LoanNumber},{loan.LoanGuid},{loanFolder}");
////        }
////    }
////}


////void WriteToCsvWithClosingDate(string inputFilePath, string outputFilePath)
////{
////    var loans = ParseCsv(inputFilePath);
////    Console.WriteLine($"Found a total of {loans.Count} in file{inputFilePath}\n");

////    using (var writer = new StreamWriter(outputFilePath))
////    {
////        writer.WriteLine("LoanNumber,LoanGuid,LoanFolder,ClosingDate");
////        var count = 0;

////        foreach (var loan in loans)
////        {
////            count++;
////            Console.WriteLine($"Querying loan {count} of total {loans.Count}");
////            string closingDate = QueryClosingDate(loan.LoanGuid.ToString());

////            writer.WriteLine($"{loan.LoanNumber},{loan.LoanGuid},{loan.LoanFolder},{closingDate}");
////        }
////    }
////}


////List<LoanData> FindClosingDate(List<LoanData> loans)
////{
////    var counter = 0;
////    foreach (var loan in loans)
////    {
////        counter++;
////        Console.WriteLine($"Querying loan {counter} of total {loans.Count}");
////        var date = QueryClosingDate(loan.LoanGuid.ToString());
////        loan.ClosingDate = date;
////    }
////    return loans;
////}

//public record LoanData
//{
//    public string LoanNumber { get; set; }
//    public Guid LoanGuid { get; set; }
//    public string LoanFolder { get; set; }
//    public string ClosingDate { get; set; }
//}
#endregion