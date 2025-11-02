using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Data.Entity; // #region Assembly EntityFramework, Version=6.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Data.Entity.Validation;
using System.Data.Entity.Infrastructure;

// Simplified version of a custom exception.
public class DbContextSaveChangesWrapperException: Exception // or DataException
{
  public DbContextSaveChangesWrapperException(string message) : base(message)
  {}
}

public partial class DbContextSaveChangesWrapper {
  // private static readonly Regex TrailingZerosRegex = new Regex(@"\.0+", RegexOptions.Compiled);
  [GeneratedRegex(@"\.0+")]
  private static partial Regex TrailingZerosRegex();

  private static DateTime CurrentLocalDateTime() => DateTime.Now; // Simplified version of getting current local date time..

  private const string ErrorCodeSeparator = " ◙ ";

  public static async Task SaveChangesWithClearerErrorMessageAsync(DbContext dbContext, ILogger logger)
  {
    try
    {
      await dbContext.SaveChangesAsync();
    }
    catch (System.Data.Entity.Validation.DbEntityValidationException e)
    {
      var localTimestamp = CurrentLocalDateTime().ToString("yyyy-MM-ddTHH:mm:ss.fff");
      
      // Basic error log:
      var errorMessage = $"Error code {localTimestamp}. Cannot validate the data.";
      logger.LogError(errorMessage);

      errorMessage = GetErrorMessageFromDbEntityValidationException(dbContext, localTimestamp, e);
      if (errorMessage != null)
      {
        // Clearer error log:
        logger.LogError(e, errorMessage);
      }

      // Throwing clearer error message:
      throw new DbContextSaveChangesWrapperException(errorMessage);
      // For example: Error code 2025-11-02T22:09:44.047. The field code 'ShippedReference ◙ V94SDR' with value '123456789' should be text with a maximum length of '8'. The field code 'ExpectedReference ◙ V94XDR' with value '123456789' should be text with a maximum length of '8'.
    }
    catch (System.Data.Entity.Infrastructure.DbUpdateException e)
    {
      var localTimestamp = CurrentLocalDateTime().ToString("yyyy-MM-ddTHH:mm:ss.fff");
      
      // Basic error log:
      var errorMessage = $"Error code {localTimestamp}. Cannot update the data.";
      logger.LogError(errorMessage);

      errorMessage = GetErrorMessageFromDbUpdateException(dbContext, localTimestamp, e);
      if (errorMessage != null)
      {
        // Clearer error log:
        logger.LogError(e, errorMessage);
      }

      // Throwing clearer error message:
      throw new DbContextSaveChangesWrapperException(errorMessage);
      // For example: Error code 2025-11-02T22:09:44.047 ◙ CubicMeasurement ◙ V93CUB. Parameter value '16.9166666667' is out of range.
    }
    catch (Exception) {
      throw;
    }
  }

  private static string GetErrorMessageFromDbEntityValidationException(DbContext dbContext, string localTimestamp, DbEntityValidationException e)
  {
    // Basic approach: just present the first error.
    // var msg = string.Join(Environment.NewLine, e.EntityValidationErrors.First().ValidationErrors.Select(ve => ve.ErrorMessage));
    // e.g. "The field ShippedReferencevvvV94SDR must be a string or array type with a maximum length of '8'.\nThe field ExpectedReferencevvvV94XDR must be a string or array type with a maximum length of '8'."

    // Improved approach: present all the errors.
    var msg = System.Text.Json.JsonSerializer.Serialize(e.EntityValidationErrors.Select(eve => eve.ValidationErrors));
    // e.g. "[[{\"PropertyName\":\"ShippedReferencevvvV94SDR\",\"ErrorMessage\":\"The field ShippedReferencevvvV94SDR must be a string or array type with a maximum length of \\u00278\\u0027.\"},{\"PropertyName\":\"ExpectedReferencevvvV94XDR\",\"ErrorMessage\":\"The field ExpectedReferencevvvV94XDR must be a string or array type with a maximum length of \\u00278\\u0027.\"}]]"

    const string MUST = "must";
    const string SHOULD = "should";
    const string STRING = " a string or array type ";
    const string TEXT = " text ";
    var clearerErrorMessage = string.Empty;
    var errorMessageList = new List<string>();
    foreach (var entityError in e.EntityValidationErrors)
    {
      var entry = entityError.Entry;
      var validationErrorList = entityError.ValidationErrors.ToList();
      foreach (var validationError in validationErrorList)
      {
        var originalErrorMessage = validationError.ErrorMessage;
        if (string.IsNullOrWhiteSpace(originalErrorMessage))
        {
          continue;
        }

        var infoStartIndex = originalErrorMessage.IndexOf(MUST);
        if (infoStartIndex == -1)
        {
          continue;
        }

        var propName =validationError.PropertyName;
        var suggestion = originalErrorMessage
          .Substring(infoStartIndex)
          .Replace(MUST, SHOULD)
          .Replace(STRING, TEXT)
          ;
        clearerErrorMessage = $"The field code '{(propName.Replace("vvv", ErrorCodeSeparator))}' with value '{entry.CurrentValues[propName]}' {suggestion}";
        errorMessageList.Add(clearerErrorMessage);
      }
    }

    if (errorMessageList.Count > 0)
    {
      clearerErrorMessage = $"Error code {localTimestamp}.{Environment.NewLine}{string.Join(" ", errorMessageList)}";
      return clearerErrorMessage;
    }
    else
    {
      return null;
    }
  }

  private static string GetErrorMessageFromDbUpdateException(DbContext dbContext, string localTimestamp, DbUpdateException e)
  {
    var innerException = e.InnerException;
    var basicErrorMessage = string.Empty;
    while (innerException != null)
    {
      // basicErrorMessage = $"Error code {localTimestamp}. {(TrailingZerosRegex.Replace(innerException.Message, ""))}";
      basicErrorMessage = $"Error code {localTimestamp}. {(TrailingZerosRegex().Replace(innerException.Message, ""))}";

      if (innerException is ArgumentException argumentException)
      {
        var changedEntryList = dbContext.ChangeTracker.Entries().Where(ent => ent.State == EntityState.Added || ent.State == EntityState.Modified).ToList();
        // For example: changedEntryList[1].Property("CartonCountvvvV74CTNS").CurrentValue

        var keyValueDict = new Dictionary<string, string>();
        foreach (var ent in changedEntryList)
        {
          var propNameList = ent.CurrentValues.PropertyNames.ToList();
          foreach (var propName in propNameList)
          {
            var prop = ent.Property(propName);
            var propHasNewValue =
              (ent.State == EntityState.Added || prop.IsModified)
              && prop.CurrentValue != null
              ;
            if (propHasNewValue)
            {
              keyValueDict.AddOrUpdate(propName, prop.CurrentValue.ToString());
            }
          }
        }

        // var changedRecordList = changedEntryList.Select(ent => ent.Entity).ToList();
        // var changedRecordListJsonString = string.Empty;

        // Using System.Text.Json below causes ERROR due to 'object cycle'!
        // try
        // {
        //   changedRecordListJsonString = System.Text.Json.JsonSerializer.Serialize(
        //     changedRecordList,
        //     new System.Text.Json.JsonSerializerOptions
        //     {
        //       ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        //       PropertyNamingPolicy = null,
        //       DictionaryKeyPolicy = null,
        //       DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        //       WriteIndented = true,
        //       // MaxDepth = 1, // NOT recommended for entity object.
        //     }
        //   );
        //   changedRecordListJsonString += string.Empty;
        // } catch {}

        // Using Newtonsoft JSON below causes ERROR!
        // try
        // {
        //   changedRecordListJsonString = Newtonsoft.Json.JsonConvert.SerializeObject(
        //     changedRecordList,
        //     Newtonsoft.Json.Formatting.Indented,
        //     new Newtonsoft.Json.JsonSerializerSettings
        //     {
        //       ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
        //       PreserveReferencesHandling = Newtonsoft.Json.PreserveReferencesHandling.None,
        //       NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
        //       // MaxDepth = 1, // NOT recommended for entity object.
        //     }
        //   );
        //   changedRecordListJsonString += string.Empty;
        // } catch {}

        foreach (var keyValue in keyValueDict)
        {
          if (basicErrorMessage.ToLower().Contains($"'{keyValue.Value.ToLower()}'"))
          {
            var clearerErrorMessage = $"Error code {localTimestamp}{ErrorCodeSeparator}{keyValue.Key}. {innerException.Message}";
            clearerErrorMessage = clearerErrorMessage.Replace("vvv", ErrorCodeSeparator);
            return clearerErrorMessage;
          }
        }
      }
      
      innerException = innerException.InnerException;
    }

    return null;
  }
}