using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using Z.BulkOperations;

namespace Demo.SinjulMSBH.BulkSynchronize
{
	public class Program
	{
		static readonly DateTimeOffset dateConst = DateTimeOffset.Now;

		public static async Task Main()
		{
			using (var context = new EntityContext())
			{
				await context.Database.EnsureDeletedAsync();
				await context.Database.EnsureCreatedAsync();
			}

			await GenerateCustomersAsync(3);

			IList<Customer> customizeToSynchronize = new List<Customer>();

			for (int i = 0; i < 2; i++)
				customizeToSynchronize.Add(new Customer()
				{
					CustomerID = (i + 1),
					Code = "Code_" + i,
					FirstName = "Updated_FirstName_" + i,
					LastName = "Updated_LastName_" + i,
					//CreatedDate = dateConst,
					//UpdatedDate = dateConst,
				});

			for (int i = 0; i < 2; i++)
				customizeToSynchronize.Add(new Customer()
				{
					Code = "New_Code_" + i,
					FirstName = "New_FirstName_" + i,
					LastName = "New_LastName_" + i,
					CreatedDate = DateTimeOffset.Now,
					UpdatedDate = DateTimeOffset.Now,
				});

			using (var context = new EntityContext())
			{
				CancellationTokenSource tcs = new CancellationTokenSource();
				CancellationToken token = new CancellationToken();

				Action<BulkOperation<Customer>> bulkOption = options =>
				{
					//options.BatchSize = 100;
					//options.InsertKeepIdentity = true;
					//options.ColumnPrimaryKeyExpression = customer => customer.Code;
					options.IgnoreOnSynchronizeInsertExpression = c => c.UpdatedDate;
					options.IgnoreOnSynchronizeUpdateExpression = c => c.CreatedDate;
				};

				await context.BulkSynchronizeAsync(customizeToSynchronize, bulkOption, token);

				//context.FutureAction(async x =>
				//	await context.BulkSynchronizeAsync(customizeToSynchronize, bulkOption, token)
				//);

				//context.FutureAction(x => x.Customers
				//	.Where(c => c.Code == "Code_0")
				//	.DeleteFromQuery()
				//);

				//context.ExecuteFutureAction();

				Thread.Sleep(1000);
				tcs.Cancel();
			}
		}

		public static async Task<IList<Customer>> GenerateCustomersAsync(int count)
		{
			IList<Customer> list = new List<Customer>();

			for (int i = 0; i < count; i++)
				list.Add(new Customer()
				{
					CustomerID = (i + 1),
					Code = "Code_" + i,
					FirstName = "FirstName_" + i,
					LastName = "LastName_" + i,
					CreatedDate = dateConst,
					UpdatedDate = dateConst,
				});

			using (var context = new EntityContext())
				await context.BulkInsertAsync(list, options => options.InsertKeepIdentity = true);

			return list;
		}

		public class EntityContext : DbContext
		{
			public EntityContext()
			{
			}

			protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
			{
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
