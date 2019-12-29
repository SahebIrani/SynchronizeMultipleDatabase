using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using Z.BulkOperations;

namespace Demo.SinjulMSBH.BulkSynchronize
{
	public class EntityContext : DbContext
	{
		public EntityContext() { }

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
		public DateTimeOffset CreatedDate { get; set; }
		public DateTimeOffset UpdatedDate { get; set; }
	}

	public class Program
	{
		public static async Task Main()
		{
			using (var context = new EntityContext())
			{
				await context.Database.EnsureDeletedAsync();
				await context.Database.EnsureCreatedAsync();
			}

			await GenerateCustomersAsync(4);

			IList<Customer> customizeToSynchronize = new List<Customer>();

			for (int i = 0; i < 2; i++)
				customizeToSynchronize.Add(new Customer()
				{
					CustomerID = (i + 1),
					Code = "SinjulMSBH_" + i,
					FirstName = "Updated_SinjulMSBH_" + i,
					LastName = "Updated_MSBH_" + i,
					CreatedDate = DateTimeOffset.Now,
					UpdatedDate = DateTimeOffset.Now,
				});

			for (int i = 0; i < 2; i++)
				customizeToSynchronize.Add(new Customer()
				{
					//CustomerID = 1000 + i,
					Code = "New_SinjulMSBH_" + i,
					FirstName = "New_Sinjul_" + i,
					LastName = "New_MSBH_" + i,
					CreatedDate = DateTimeOffset.Now,
					UpdatedDate = DateTimeOffset.Now,
				});

			using (var context = new EntityContext())
			{
				CancellationTokenSource tcs = new CancellationTokenSource();
				CancellationToken token = new CancellationToken();

				Expression<Func<Customer, object>> columnInputExpression =
					c => new { c.Code, c.FirstName, c.LastName };

				Action<BulkOperation<Customer>> bulkOption = options =>
				{
					options.BatchSize = 100;
					options.InsertKeepIdentity = false;
					options.ColumnPrimaryKeyExpression = customer => customer.Code;
					//options.ColumnInputExpression = columnInputExpression;
					//options.IgnoreOnSynchronizeUpdateExpression = c => c.CreatedDate;
					//options.IgnoreOnSynchronizeInsertExpression = c => c.UpdatedDate;
				};

				await context.BulkSynchronizeAsync(customizeToSynchronize, bulkOption, token);

				context.FutureAction(async x =>
					await context.BulkSynchronizeAsync(customizeToSynchronize, bulkOption, token)
				);

				context.FutureAction(async x =>
					await x.Customers.AsNoTracking().Where(c => c.Code == "SinjulMSBH_0")
					.DeleteFromQueryAsync()
				);

				//context.ExecuteFutureAction();

				Thread.Sleep(800);
				tcs.Cancel();

				Console.ReadKey();
			}
		}

		public static async Task<IList<Customer>> GenerateCustomersAsync(int count)
		{
			IList<Customer> list = new List<Customer>();

			for (int i = 0; i < count; i++)
				list.Add(new Customer()
				{
					CustomerID = (i + 1),
					Code = "SinjulMSBH_" + i,
					FirstName = "Sinjul_" + i,
					LastName = "MSBH_" + i,
					CreatedDate = DateTimeOffset.Now,
					UpdatedDate = DateTimeOffset.Now,
				});

			using (EntityContext context = new EntityContext())
				await context.BulkInsertAsync(list, options => options.InsertKeepIdentity = true);

			return list;
		}

	}
}
