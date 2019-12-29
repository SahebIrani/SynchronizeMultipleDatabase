using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.EntityFrameworkCore;

namespace Demo.SinjulMSBH.BulkSynchronize
{
	public class Program
	{
		public static async System.Threading.Tasks.Task Main()
		{
			using (var context = new EntityContext())
			{
				context.Database.EnsureCreated();
			}

			GenerateCustomers(3);

			var customizeToSynchronize = new List<Customer>();

			for (int i = 0; i < 2; i++)
			{
				customizeToSynchronize.Add(new Customer() { CustomerID = (i + 1), Code = "Code_" + i, FirstName = "Updated_FirstName_" + i, LastName = "Updated_LastName_" + i });
			}

			for (int i = 0; i < 2; i++)
			{
				customizeToSynchronize.Add(new Customer() { Code = "New_Code_" + i, FirstName = "New_FirstName_" + i, LastName = "New_LastName_" + i });
			}

			using (var context = new EntityContext())
			{
				//FiddleHelper.WriteTable("1 - Customers Before", context.Customers.AsNoTracking());

				context.BulkSynchronize(customizeToSynchronize);

				CancellationTokenSource tcs = new CancellationTokenSource();
				CancellationToken token = new CancellationToken();

				await context.BulkSynchronizeAsync(customizeToSynchronize, token);
				await context.BulkSynchronizeAsync(customizeToSynchronize, options =>
				{
					options.BatchSize = 100;
					options.ColumnPrimaryKeyExpression = customer => customer.Code;
					options.InsertKeepIdentity = true;
					options.IgnoreOnSynchronizeInsertExpression = c => c.UpdatedDate;
					options.IgnoreOnSynchronizeUpdateExpression = c => c.CreatedDate;
					options.ColumnPrimaryKeyExpression = c => c.Code;

				}, token);

				Thread.Sleep(1000);
				tcs.Cancel();

				context.FutureAction(x => x.BulkSynchronize(customers));
				context.FutureAction(x => x.BulkSynchronize(invoices));
				// ...code...
				context.ExecuteFutureAction();

				// ...code...
				context.FutureAction(x => context.BulkSynchronize(customizeToSynchronize, options =>
				{
					options.ColumnPrimaryKeyExpression = customer => customer.Code;
				}));
				// ...code...
				context.FutureAction(x => x.Customers.Where(c => c.Code == "Code_0").DeleteFromQuery());
				context.ExecuteFutureAction();

				//FiddleHelper.WriteTable("2 - Customers After", context.Customers.AsNoTracking());
			}
		}

		public static List<Customer> GenerateCustomers(int count)
		{
			var list = new List<Customer>();

			for (int i = 0; i < count; i++)
			{
				list.Add(new Customer() { CustomerID = (i + 1), Code = "Code_" + i, FirstName = "FirstName_" + i, LastName = "LastName_" + i });
			}

			using (var context = new EntityContext())
			{
				context.BulkInsert(list, options => options.InsertKeepIdentity = true);
			}


			return list;
		}

		public class EntityContext : DbContext
		{
			public EntityContext()
			{
			}

			protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
			{
				//optionsBuilder.UseSqlServer(new SqlConnection(FiddleHelper.GetConnectionStringSqlServer()));
				optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=SyncDb;Trusted_Connection=True;MultipleActiveResultSets=true");

				base.OnConfiguring(optionsBuilder);
			}

			public DbSet<Customer> Customers { get; set; }
		}

		public class Customer
		{
			public int CustomerID { get; set; }
			public string Code { get; set; }
			public string FirstName { get; set; }
			public string LastName { get; set; }
			public DateTimeOffset UpdatedDate { get; set; }
			public DateTimeOffset CreatedDate { get; set; }
		}
	}
}
