using System.Linq;

namespace System.Collections.Generic
{
    internal static class IEnumerableExtensions
    {
        /// <summary>
        /// Generates the Cartesian product of an enumerable sequence which itself contains enumerable values, generating all
        /// possible combinations.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Borrowed from <see href="https://stackoverflow.com/a/4424005/1136311">Cartesian Product + N x M Dynamic Array - Stack
        /// Overflow</see> answer by user <see href="https://stackoverflow.com/users/310574/gabe">Gabe</see>, licensed
        /// under <see href="https://creativecommons.org/licenses/by-sa/2.5/">CC BY-SA 2.5</see>. Changes have been made only to
        /// the source formatting.
        /// </para>
        /// </remarks>
        /// <typeparam name="T">The type of values represented by the inner enumerable set.</typeparam>
        /// <param name="sequences">The enumerable sequence from which to generate the Cartesian product.</param>
        /// <returns>An enumerable sequence representing the Cartesian product.</returns>
        public static IEnumerable<IEnumerable<T>> CartesianProduct<T>(this IEnumerable<IEnumerable<T>> sequences)
        {
            IEnumerable<IEnumerable<T>> emptyProduct = new[] { Enumerable.Empty<T>() };
            return sequences.Aggregate
            (
                emptyProduct,
                (accumulator, sequence) =>
                    from accseq in accumulator
                    from item in sequence
                    select accseq.Concat(new[] { item })
            );
        }
    }
}