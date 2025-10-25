using Akagi.Web.Data;
using Akagi.Web.Services.Users;
using MongoDB.Driver;
using System.ComponentModel.DataAnnotations;

namespace Akagi.Web.Models.TimeTrackers;

public class UniqueDefinitionNameAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string name)
        {
            return new ValidationResult("Not a string.");
        }

        DefinitionModel definitionModel = (DefinitionModel)validationContext.ObjectInstance;
        IDefinitionDatabase? definitionDatabase = validationContext.GetService<IDefinitionDatabase>();
        IUserState? userState = validationContext.GetService<IUserState>();

        if (definitionDatabase is null || userState is null)
        {
            return new ValidationResult("Unable to validate definition name.");
        }

        User user = userState.GetCurrentUserAsync()
                             .GetAwaiter()
                             .GetResult();

        FilterDefinition<Definition> filterDefinition = Builders<Definition>.Filter.And(
            Builders<Definition>.Filter.Eq(d => d.Name, name),
            Builders<Definition>.Filter.Eq(d => d.UserId, user.Id)
        );
        Definition? existingDefinition = definitionDatabase.GetDocumentsByPredicateAsync(filterDefinition)
                                                           .GetAwaiter()
                                                           .GetResult()
                                                           .FirstOrDefault();

        if (existingDefinition is not null)
        {
            return new ValidationResult("You already have a definition with this name.");
        }

        return ValidationResult.Success;
    }
}
