Sometimes we work on projects to improve or add features to live apps, where the database schema and the entity framework (ORM) are already defined and fixed.

Imagine a situation where the project deals with an existing usage of **.NET Entity Framework 6** and interacts with numerous legacy **SQL Server** fields defined as **DECIMAL(x, 0)** , which practically expect whole numbers without any decimal digit.
The project operates under a tight deadline, leaving no room for negotiation.

During development, it is found that certain processes and calculations may assign values such as "16.9166666667" to these zero-scale decimal fields.
When this happens, the app outputs a "cryptic" error message:
"System.Data.Entity.Infrastructure.DbUpdateException: An error occurred while updating the entries. See the inner exception for details."

Addressing all these zero-scale decimal fields one-by-one (e.g. to apply rounding or validation) is not feasible.
The project lead decides that while this error scenario itself is acceptable, the app needs to be able to present end users with clear and informative error message (mentioning the problematic value and the corresponding field).

**So, what could be done to improve the outcome of this situation?**

Upon investigating the inner exceptions, we can see that the innermost exception typically contains an error message like:
"Parameter value '16.9166666667' is out of range."

Unfortunately, this default error message from .NET Entity Framework version 6  only reports the invalid value without mentioning the field to which that invalid value is being assigned.

**What if we could find an approach to inspect the database context of Entity Framework version 6 further to find that field then present the name of that field, along with the problematic value, to the end users?**

**And once we have found that approach, what if we could use similar approach to clarify other types (variations) of errors caused by saving values that do not match the defined database fields?**
