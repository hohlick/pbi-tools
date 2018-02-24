﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PbixTools.Tests
{
    public static class TestData
    {
        public const string MinimalMashupPackage =
            @"UEsDBBQAAgAIAKdYVkwLpf7AqwAAAPoAAAASABwAQ29uZmlnL1BhY2thZ2UueG1sIKIYACigFAAAAAAAAAAAAAAAAAAAAAAAAAAAAIWPQQ6CMBREr0K657cFS5R8SqILN5KYmBi3BCs0QjG0CHdz4ZG8giaKcedu5uUtZh63O6ZjU3tX1VndmoRwYMRTpmiP2pQJ6d3Jn5NU4jYvznmpvJdsbDzaY0Iq5y4xpcMwwBBC25U0YIzTQ7bZFZVqcvKV9X/Z18a63BSKSNy/x8gAhADBOINoxpFOGDNtpsxBQBgsImBIfzCu+tr1nZLK+Osl0qki/fyQT1BLAwQUAAIACACnWFZMD8rpq6QAAADpAAAAEwAcAFtDb250ZW50X1R5cGVzXS54bWwgohgAKKAUAAAAAAAAAAAAAAAAAAAAAAAAAAAAbY5LDsIwDESvEnmfurBACDVlAdyAC0TB/Yjmo8ZF4WwsOBJXIG13iKVn5nnm83pXx2QH8aAx9t4p2BQlCHLG33rXKpi4kXs41tX1GSiKHHVRQcccDojRdGR1LHwgl53Gj1ZzPscWgzZ33RJuy3KHxjsmx5LnH1BXZ2r0NLC4pCyvtRkHcVpzc5UCpsS4yPiXsD95HcLQG83ZxCRtlHYhcRlefwFQSwMEFAACAAgAp1hWTCiKR7gOAAAAEQAAABMAHABGb3JtdWxhcy9TZWN0aW9uMS5tIKIYACigFAAAAAAAAAAAAAAAAAAAAAAAAAAAACtOTS7JzM9TCIbQhtYAUEsBAi0AFAACAAgAp1hWTAul/sCrAAAA+gAAABIAAAAAAAAAAAAAAAAAAAAAAENvbmZpZy9QYWNrYWdlLnhtbFBLAQItABQAAgAIAKdYVkwPyumrpAAAAOkAAAATAAAAAAAAAAAAAAAAAPcAAABbQ29udGVudF9UeXBlc10ueG1sUEsBAi0AFAACAAgAp1hWTCiKR7gOAAAAEQAAABMAAAAAAAAAAAAAAAAA6AEAAEZvcm11bGFzL1NlY3Rpb24xLm1QSwUGAAAAAAMAAwDCAAAAQwIAAAAA";

        public const string GlobalPipe = "7f3b9dee-6ed9-4965-9caa-c1b33a5c34ad";

        public static readonly byte[] MinimalMashupPackageBytes = Convert.FromBase64String(MinimalMashupPackage);

    }
}
