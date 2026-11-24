using OpenWards.Sim.Commands;
using OpenWards.Sim.Economy;

namespace OpenWards.Sim.Tests;

/// <summary>Loans from the bank, interest and overdrafts.</summary>
public class LoanTests
{
    [Fact]
    public void AHospitalMayBorrowAThirdOfItsValueAndTenThousand()
    {
        var hospital = new Hospital(0, 1000, value: 50_000);
        Assert.Equal(26_666, hospital.MaxLoan); // floor(50,000 / 3) + 10,000

        hospital.Borrow(1, asMuchAsAllowed: false);
        Assert.Equal((5000, 6000L), (hospital.Loan, hospital.Balance));
        Assert.Equal(new Transaction(1, TransactionKind.Loan, string.Empty, 5000, 6000), hospital.Transactions[0]);

        hospital.Borrow(2, asMuchAsAllowed: true);
        Assert.Equal((26_666, 27_666L), (hospital.Loan, hospital.Balance));

        // No more: the loan may not grow past the limit.
        hospital.Borrow(3, asMuchAsAllowed: false);
        Assert.Equal(26_666, hospital.Loan);
        Assert.Equal(2, hospital.Transactions.Count);
    }

    [Fact]
    public void NoHospitalMayBorrowMoreThanAHundredThousand()
    {
        Assert.Equal(100_000, new Hospital(0, 0, value: 600_000).MaxLoan);
        Assert.Equal(10_000, new Hospital(0, 0, value: 0).MaxLoan);
    }

    [Fact]
    public void LoansArePaidBackInStepsOfFiveThousand()
    {
        var hospital = new Hospital(0, 0, value: 60_000);
        hospital.Borrow(1, asMuchAsAllowed: true);
        Assert.Equal(30_000, hospital.Loan);

        hospital.Repay(2, asMuchAsPossible: false);
        Assert.Equal((25_000, 25_000L), (hospital.Loan, hospital.Balance));

        // With 17,000 in the bank as much as possible is 15,000.
        hospital.Spend(3, 8000, TransactionKind.Wages, string.Empty);
        hospital.Repay(4, asMuchAsPossible: true);
        Assert.Equal((10_000, 2000L), (hospital.Loan, hospital.Balance));

        // Without 5,000 in the bank nothing is paid back.
        hospital.Repay(5, asMuchAsPossible: false);
        hospital.Repay(5, asMuchAsPossible: true);
        Assert.Equal(10_000, hospital.Loan);
        Assert.Equal(TransactionKind.LoanRepayment, hospital.Transactions[0].Kind);
    }

    [Fact]
    public void InterestIsPaidMonthlyAndAnOverdraftCostsTwiceTheRate()
    {
        var hospital = new Hospital(0, 0, value: 100_000);
        hospital.Borrow(1, asMuchAsAllowed: true);
        Assert.Equal(43_333, hospital.Loan);
        hospital.Spend(1, 43_333 + 36_500, TransactionKind.Wages, string.Empty);
        Assert.Equal(-36_500, hospital.Balance);

        hospital.PayInterest(100);

        // floor(100 × 43,333 / 120,000) = 36, then on the balance that leaves floor(100 × 36,536 × 2 / 120,000) = 60.
        Assert.Equal(new Transaction(100, TransactionKind.LoanInterest, string.Empty, -36, -36_536), hospital.Transactions[1]);
        Assert.Equal(new Transaction(100, TransactionKind.Overdraft, string.Empty, -60, -36_596), hospital.Transactions[0]);
    }

    [Fact]
    public void InterestIsRoundedDownAndNothingIsChargedWithoutALoanOrOverdraft()
    {
        var hospital = new Hospital(0, 0, value: 0);
        hospital.PayInterest(5);
        Assert.Empty(hospital.Transactions);

        hospital.Borrow(1, asMuchAsAllowed: false);
        hospital.Borrow(1, asMuchAsAllowed: false);
        Assert.Equal(10_000, hospital.Loan);

        // 100 × 10,000 / 120,000 = 8.33.
        hospital.PayInterest(10);
        Assert.Equal(new Transaction(10, TransactionKind.LoanInterest, string.Empty, -8, 9992), hospital.Transactions[0]);
    }

    [Fact]
    public void TheLevelSetsTheRatesAndCommandsBorrowAndRepay()
    {
        var map = TestMaps.FromRows("....", "....");
        var rules = new LevelRules(0, [], [], interestRate: 365, overdraftDifferential: 0, endConditions: new EndConditions([new(EndCriterion.Balance, 1, true, 0, 0, 0)], []));
        var simulation = new Simulation(map, seed: 1, rules: rules);
        var hospital = simulation.Hospitals[0];
        Assert.Equal(365, hospital.InterestRate);

        simulation.Schedule(0, new BorrowCommand(AsMuchAsAllowed: true));
        simulation.Step();
        Assert.Equal(16_666, hospital.Loan); // floor(20,000 / 3) + 10,000 on the value of 20,000

        // The month's interest is paid at its end, after the wages: floor(365 × 16,666 / 120,000) = 50.
        while (simulation.Date.Month == 1)
        {
            simulation.Step();
        }

        Assert.Equal(new Transaction(simulation.Tick - 1, TransactionKind.LoanInterest, string.Empty, -50, 16_666 - 50), hospital.Transactions[0]);

        // The balance goal is met at the end of March, but a hospital with a loan does not win.
        while (simulation.Date.Month < 4)
        {
            simulation.Step();
        }

        Assert.Equal(EndOutcome.None, hospital.Outcome);

        simulation.Schedule(0, new RepayCommand(AsMuchAsPossible: true));
        simulation.Step();
        Assert.Equal(1666, hospital.Loan);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(1666, restored.Hospitals[0].Loan);
        Assert.Equal(365, restored.Rules.InterestRate);
    }
}
