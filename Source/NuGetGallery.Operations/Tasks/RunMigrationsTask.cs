﻿using System;
using System.Collections.Generic;
using System.Data.Entity.Migrations;
using System.Data.Entity.Migrations.Infrastructure;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NuGetGallery.Operations.Common;

namespace NuGetGallery.Operations.Tasks
{
    [Command("runmigrations", "Executes migrations against a database", AltName = "rm", MaxArgs = 0)]
    public class RunMigrationsTask : MigrationsTask
    {
        private static readonly Regex MigrationIdRegex = new Regex(@"(?<timestamp>\d+)_(?<name>.*)");
        [Option("The target to migrate the database to. Timestamp does not need to be specified.", AltName = "m")]
        public string TargetMigration { get; set; }

        [Option("Set this to generate a SQL File instead of running the migration. The file will be dropped in the current directory and named [Source]-[TargetMigration].sql")]
        public bool Sql { get; set; }

        public override void ValidateArguments()
        {
            base.ValidateArguments();
            ArgCheck.Required(TargetMigration, "TargetMigration");
        }

        protected override void ExecuteCommandCore(MigratorBase migrator)
        {
            if (Sql)
            {
                ScriptMigrations(migrator);
            }
            else
            {
                RunMigrations(migrator);
            }
        }

        private void ScriptMigrations(MigratorBase migrator)
        {
            var scriptingMigrator = new MigratorScriptingDecorator(migrator);

            // Determine the start migration
            var start = migrator.GetDatabaseMigrations().FirstOrDefault();

            // Determine the target
            var target = migrator.GetLocalMigrations().Single(s => IsMigration(s, TargetMigration));

            string scriptFileName = String.Format("{0}-{1}.sql", start ?? "0", target);
            if(File.Exists(scriptFileName)) {
                Log.Error("File already exists: {0}", scriptFileName);
                return;
            }

            // Generate script
            Log.Info("Scripting migration from {0} to {1}", start ?? "<Nothing>", target);
            if (!WhatIf)
            {
                string script = scriptingMigrator.ScriptUpdate(start, target);

                // Write the script
                File.WriteAllText(scriptFileName, script);
            }
            Log.Info("Wrote script to {0}", scriptFileName);
        }

        private void RunMigrations(MigratorBase migrator)
        {
            // We only support UP right now.
            // Find the target migration and collect everything between the start and it
            var toApply = new List<string>();

            // TakeWhile won't work because it doesn't include the actual target :(
            foreach (var migration in migrator.GetPendingMigrations())
            {
                toApply.Add(migration);
                if (IsMigration(migration, TargetMigration))
                {
                    break;
                }
            }

            if (!toApply.Any(s => IsMigration(s, TargetMigration)))
            {
                Log.Error("{0} is not a pending migration. Only the UP direction is supported at the moment.", TargetMigration);
                return;
            }

            // We have a list of migrations to apply, apply them one-by-one
            foreach (var migration in toApply)
            {
                Log.Info("Applying {0}", migration);
                if (!WhatIf)
                {
                    migrator.Update(migration);
                }
            }
            Log.Info("All requested migrations applied");
        }

        private bool IsMigration(string migrationId, string target)
        {
            // Get the shortname
            var match = MigrationIdRegex.Match(migrationId);
            if (!match.Success)
            {
                return String.Equals(migrationId, target, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                var name = match.Groups["name"].Value;
                return String.Equals(name, target, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
