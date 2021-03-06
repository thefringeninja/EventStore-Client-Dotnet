using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using Xunit;

namespace EventStore.Client.PersistentSubscriptions {
	public class happy_case_catching_up_to_link_to_events_auto_ack
		: IClassFixture<happy_case_catching_up_to_link_to_events_auto_ack.Fixture> {
		private const string Stream = nameof(happy_case_catching_up_to_link_to_events_auto_ack);
		private const string Group = nameof(Group);
		private const int BufferCount = 10;
		private const int EventWriteCount = BufferCount * 2;

		private readonly Fixture _fixture;

		public happy_case_catching_up_to_link_to_events_auto_ack(Fixture fixture) {
			_fixture = fixture;
		}

		[Fact]
		public async Task Test() {
			await _fixture.EventsReceived.WithTimeout();
		}

		public class Fixture : EventStoreGrpcFixture {
			private readonly EventData[] _events;
			private readonly TaskCompletionSource<bool> _eventsReceived;
			public Task EventsReceived => _eventsReceived.Task;

			private PersistentSubscription _subscription;
			private int _eventReceivedCount;

			public Fixture() {
				_events = CreateTestEvents(EventWriteCount)
					.Select((e, i) => new EventData(e.EventId, SystemEventTypes.LinkTo,
						Helper.UTF8NoBom.GetBytes($"{i}@{Stream}"),
						contentType: Constants.Metadata.ContentTypes.ApplicationOctetStream))
					.ToArray();
				_eventsReceived = new TaskCompletionSource<bool>();
			}

			protected override async Task Given() {
				foreach (var e in _events) {
					await Client.AppendToStreamAsync(Stream, AnyStreamRevision.Any, new[] {e});
				}

				await Client.PersistentSubscriptions.CreateAsync(Stream, Group,
					new PersistentSubscriptionSettings(startFrom: StreamRevision.Start, resolveLinkTos: true),
					TestCredentials.Root);
				_subscription = Client.PersistentSubscriptions.Subscribe(Stream, Group,
					(subscription, e, retryCount, ct) => {
						if (Interlocked.Increment(ref _eventReceivedCount) == _events.Length) {
							_eventsReceived.TrySetResult(true);
						}

						return Task.CompletedTask;
					}, (s, r, e) => {
						if (e != null) {
							_eventsReceived.TrySetException(e);
						}
					},
					bufferSize: BufferCount,
					userCredentials: TestCredentials.Root);
			}

			protected override Task When() => Task.CompletedTask;

			public override Task DisposeAsync() {
				_subscription?.Dispose();
				return base.DisposeAsync();
			}
		}
	}
}
