<script setup lang="ts">
import { ref } from 'vue';
import { api, type Order } from '../api';

const props = withDefaults(defineProps<{
  orders: Order[];
  currentCustomerId?: number | null;
  title?: string;
  eyebrow?: string;
  showCustomer?: boolean;
}>(), {
  title: 'Accepted customer orders',
  eyebrow: 'Order history',
  showCustomer: true
});

const emit = defineEmits<{
  changed: [];
}>();

const error = ref('');
const loadingOrderId = ref<number | null>(null);

async function reorder(order: Order) {
  error.value = '';
  const deliveryDate = window.prompt('New delivery date in DD/MM/YYYY format', order.deliveryDate);
  if (!deliveryDate) return;

  loadingOrderId.value = order.id;
  try {
    if (props.currentCustomerId) {
      await api.myReorder(props.currentCustomerId, order.id, deliveryDate);
    } else {
      await api.reorder(order.id, deliveryDate);
    }
    emit('changed');
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Could not reorder.';
  } finally {
    loadingOrderId.value = null;
  }
}
</script>

<template>
  <section class="card wide order-history-panel">
    <div class="card-header">
      <div>
        <p class="eyebrow">{{ eyebrow }}</p>
        <h2>{{ title }}</h2>
      </div>
      <span class="badge">{{ orders.length }} valid</span>
    </div>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-if="orders.length === 0" class="hint">No accepted orders to show yet.</p>

    <div v-else class="order-card-grid">
      <article v-for="order in orders" :key="order.id" class="order-card">
        <div class="order-card-top">
          <div>
            <p class="eyebrow">Order #{{ order.id }}</p>
            <h3 v-if="showCustomer">{{ order.customerName }}</h3>
            <h3 v-else>Delivery {{ order.deliveryDate }}</h3>
            <small>
              {{ order.deliveryAddress.city }} · {{ order.deliveryAddress.postcode }}
            </small>
          </div>

          <div class="order-total">
            <span>Total</span>
            <strong>£{{ order.total.toFixed(2) }}</strong>
          </div>
        </div>

        <div class="order-meta">
          <span>{{ order.deliveryDate }}</span>
          <span>{{ order.items.length }} items</span>
          <span>{{ order.stockReservations.length }} reservations</span>
        </div>

        <div class="order-items">
          <div v-for="item in order.items" :key="`${order.id}-${item.productSku}`">
            <span>{{ item.productName }}</span>
            <strong>× {{ item.quantity }}</strong>
          </div>
        </div>

        <div v-if="order.stockReservations.length" class="reservation-strip">
          <span v-for="reservation in order.stockReservations" :key="`${order.id}-${reservation.stockBatchId}`">
            {{ reservation.stockBatchId }} · {{ reservation.quantity }}
          </span>
        </div>

        <button
          class="secondary compact"
          :disabled="loadingOrderId === order.id"
          @click="reorder(order)"
        >
          {{ loadingOrderId === order.id ? 'Reordering...' : 'Reorder basket' }}
        </button>
      </article>
    </div>
  </section>
</template>