using AliantWebS.Models;
using Xunit;

namespace Shared.Tests;

/// <summary>
/// Walks over an element tree - down through InverseParent, up through ParentId - on data whose parents
/// can run in a circle. adbo.Element accepts that: FK_Element_Element1 only asks that the parent exists, and
/// a cut element pasted into its own branch was saved that way. A walk that followed the circle never
/// ended, and the stack overflow ended the web process with it.
/// </summary>
public class ElementTreeTests
{
    private static Element E(string name) => new() { ElementId = Guid.NewGuid(), Name = name };

    /// <summary>What EF's fix-up leaves once the rows are loaded: the id, both navigations.</summary>
    private static void Attach(Element child, Element parent)
    {
        child.ParentId = parent.ElementId;
        child.Parent = parent;
        parent.InverseParent.Add(child);
    }

    private static Func<Guid, Task<Guid?>> ParentsOf(params Element[] elements)
    {
        var parents = elements.ToDictionary(e => e.ElementId, e => e.ParentId);
        return id => Task.FromResult(parents.GetValueOrDefault(id));
    }

    [Fact]
    public void An_element_that_is_its_own_parent_is_listed_once()
    {
        var a1 = E("A1");
        Attach(a1, a1);

        var children = new List<Element>();
        var ids = Helpers.Helpers.GetTheChildren(new object[] { a1 }, children);

        Assert.Equal(new Guid?[] { a1.ElementId }, ids);
        Assert.Same(a1, Assert.Single(children));
    }

    [Fact]
    public void A_circle_of_two_is_walked_once_round()
    {
        var x = E("X");
        var y = E("Y");
        var z = E("Z");
        Attach(x, y);
        Attach(y, x);
        Attach(z, y);

        var children = new List<Element>();
        var ids = Helpers.Helpers.GetTheChildren(new object[] { x }, children);

        Assert.Equal(new Guid?[] { x.ElementId, y.ElementId, z.ElementId }, ids);
        Assert.Equal(new[] { x, y, z }, children);
    }

    [Fact]
    public void An_element_below_two_of_the_starting_elements_is_listed_once()
    {
        // A and its child A1 both checked: A1's branch is reached twice.
        var a = E("A");
        var a1 = E("A1");
        var a11 = E("A11");
        Attach(a1, a);
        Attach(a11, a1);

        var children = new List<Element>();
        var ids = Helpers.Helpers.GetTheChildren(new object[] { a, a1 }, children);

        Assert.Equal(new Guid?[] { a.ElementId, a1.ElementId, a11.ElementId }, ids);
        Assert.Equal(new[] { a, a1, a11 }, children);
    }

    [Fact]
    public async Task A_cut_element_and_everything_below_it_are_inside_its_branch()
    {
        var a = E("A");
        var a1 = E("A1");
        var a11 = E("A11");
        var b = E("B");
        Attach(a1, a);
        Attach(a11, a1);
        var parentOf = ParentsOf(a, a1, a11, b);
        var cut = new HashSet<Guid> { a1.ElementId };

        Assert.True(await Helpers.Helpers.IsInAnyBranchAsync(a1.ElementId, cut, parentOf));
        Assert.True(await Helpers.Helpers.IsInAnyBranchAsync(a11.ElementId, cut, parentOf));
        Assert.False(await Helpers.Helpers.IsInAnyBranchAsync(a.ElementId, cut, parentOf));
        Assert.False(await Helpers.Helpers.IsInAnyBranchAsync(b.ElementId, cut, parentOf));
    }

    [Fact]
    public async Task Any_of_several_cut_elements_is_enough()
    {
        var a = E("A");
        var b = E("B");
        var b1 = E("B1");
        Attach(b1, b);

        var cut = new HashSet<Guid> { a.ElementId, b.ElementId };

        Assert.True(await Helpers.Helpers.IsInAnyBranchAsync(b1.ElementId, cut, ParentsOf(a, b, b1)));
    }

    [Fact]
    public async Task A_parent_chain_that_runs_in_a_circle_ends_the_walk()
    {
        var x = E("X");
        var y = E("Y");
        var z = E("Z");
        Attach(x, y);
        Attach(y, x);
        Attach(z, x);
        var parentOf = ParentsOf(x, y, z);

        // Nothing cut on the circle: the walk goes once round and answers no.
        Assert.False(await Helpers.Helpers.IsInAnyBranchAsync(z.ElementId, new HashSet<Guid> { Guid.NewGuid() }, parentOf));
        // A cut element on the circle is met on the way.
        Assert.True(await Helpers.Helpers.IsInAnyBranchAsync(z.ElementId, new HashSet<Guid> { y.ElementId }, parentOf));
    }

    [Fact]
    public async Task An_element_the_lookup_does_not_know_ends_the_walk()
    {
        var a = E("A");
        var a1 = E("A1");
        Attach(a1, a);

        // Only A1 is known: its parent A is asked for and comes back null.
        Assert.False(await Helpers.Helpers.IsInAnyBranchAsync(a1.ElementId, new HashSet<Guid> { Guid.NewGuid() }, ParentsOf(a1)));
    }
}
