export type PnlLine = { categoryId: number | null; name: string; amount: number; priorAmount: number | null; transactionCount: number };

export type ProfitLoss = {
  periodStart: string;
  periodEnd: string;
  periodLabel: string;
  projectName: string | null;
  incomeLines: PnlLine[];
  totalIncome: number;
  expenseLines: PnlLine[];
  totalExpenses: number;
  netProfit: number;
  priorTotalIncome: number;
  priorTotalExpenses: number;
  priorNetProfit: number;
};

export type BalanceSheetLine = { accountId: number; ledgerCode: string | null; name: string; amount: number };
export type BalanceSheetGroup = { name: string; lines: BalanceSheetLine[]; total: number };

export type BalanceSheet = {
  asAt: string;
  assetGroups: BalanceSheetGroup[];
  totalAssets: number;
  liabilityGroups: BalanceSheetGroup[];
  totalLiabilities: number;
  capitalLines: BalanceSheetLine[];
  retainedProfit: number;
  unpostedFixedAssetCharge: number;
  retainedProfitStart: string | null;
  totalCapital: number;
  totalLiabilitiesAndCapital: number;
  isBalanced: boolean;
  imbalance: number;
  unbalancedAccounts: string[];
};
