<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import {
  api,
  type CreateOrderRequest,
  type Customer,
  type OrderPreviewResponse,
  type Product,
  type ProductRecommendation
} from '../api';

const props = defineProps<{
  products: Product[];
  customers: Customer[];
  currentCustomer?: Customer | null;
}>();

const emit = defineEmits<{
  created: [];
}>();

const form = reactive({
  selectedCustomerId: '',
  customerName: '',
  addressLine1: '',
  postcode: '',
  city: '',
  deliveryDate: '2026-08-25',
  selectedSku: '',
  quantity: 1,
  itemTotal: 1
});

const items = ref<CreateOrderRequest['items']>([]);
const error = ref('');
const success = ref('');
const submitting = ref(false);

const preview = ref<OrderPreviewResponse | null>(null);
const checkingPreview = ref(false);
const previewError = ref('');

const assistantQuery = ref('breakfast for a cafe');
const recommendations = ref<ProductRecommendation[]>([]);
const recommendationError = ref('');
const loadingRecommendations = ref(false);

const selectedProduct = computed(() => props.products.find(p => p.sku === form.selectedSku));

const total = computed(() =>
  items.value.reduce((sum, item) => sum + Number(item.itemTotal || 0), 0)
);

const selectedCustomerIdNumber = computed(() => {
  if (props.currentCustomer) return props.currentCustomer.id;
  return form.selectedCustomerId ? Number(form.selectedCustomerId) : null;
});

function fillCustomer(customer: Customer) {
  form.selectedCustomerId = String(customer.id);
  form.customerName = customer.name;
  form.addressLine1 = customer.addressLine1;
  form.postcode = customer.postcode;
  form.city = customer.city;
}

watch(
  () => props.currentCustomer,
  (customer) => {
    if (customer) fillCustomer(customer);
  },
  { immediate: true }
);

watch(
  () => form.selectedCustomerId,
  (customerId) => {
    if (props.currentCustomer || !customerId) return;

    const customer = props.customers.find(c => c.id === Number(customerId));
    if (!customer) return;

    fillCustomer(customer);
  }
);

watch(
  () => [form.deliveryDate, items.value.length, total.value],
  () => clearPreview()
);

function toApiDate(htmlDate: string) {
  const [year, month, day] = htmlDate.split('-');
  return `${day}/${month}/${year}`;
}

function productName(sku: string) {
  return props.products.find(p => p.sku === sku)?.name ?? sku;
}

function clearPreview() {
  preview.value = null;
  previewError.value = '';
}

function buildPayload(): CreateOrderRequest | null {
  error.value = '';

  if (!form.customerName.trim() || !form.addressLine1.trim() || !form.postcode.trim() || !form.city.trim()) {
    error.value = 'Customer name and full delivery address are required.';
    return null;
  }

  if (items.value.length === 0) {
    error.value = 'Add at least one order item.';
    return null;
  }

  return {
    customerName: form.customerName,
    deliveryAddress: {
      addressLine1: form.addressLine1,
      postcode: form.postcode,
      city: form.city
    },
    deliveryDate: toApiDate(form.deliveryDate),
    items: items.value
  };
}

function addItem() {
  error.value = '';
  success.value = '';

  if (!form.selectedSku) {
    error.value = 'Choose a product before adding an item.';
    return;
  }

  if (form.quantity <= 0 || !Number.isInteger(Number(form.quantity))) {
    error.value = 'Quantity must be a positive whole number.';
    return;
  }

  if (Number(form.itemTotal) <= 0) {
    error.value = 'Item total must be greater than zero.';
    return;
  }

  items.value.push({
    productSku: form.selectedSku,
    quantity: Number(form.quantity),
    itemTotal: Number(Number(form.itemTotal).toFixed(2))
  });

  clearPreview();

  form.selectedSku = '';
  form.quantity = 1;
  form.itemTotal = 1;
}

function addRecommendation(recommendation: ProductRecommendation) {
  form.selectedSku = recommendation.sku;
  form.quantity = 1;
  form.itemTotal = Number((recommendation.suggestedUnitPrice ?? 1).toFixed(2));
  addItem();
}

function useRecommendation(recommendation: ProductRecommendation) {
  form.selectedSku = recommendation.sku;
  form.quantity = 1;
  form.itemTotal = Number((recommendation.suggestedUnitPrice ?? 1).toFixed(2));
  clearPreview();
}

function removeItem(index: number) {
  items.value.splice(index, 1);
  clearPreview();
}

async function findRecommendations() {
  recommendationError.value = '';
  loadingRecommendations.value = true;

  try {
    const customerId = selectedCustomerIdNumber.value;

    recommendations.value = customerId
      ? await api.myRecommendations(customerId, assistantQuery.value, 6)
      : await api.recommendations(assistantQuery.value, null, 6);
  } catch (err) {
    recommendationError.value = err instanceof Error ? err.message : 'Could not load recommendations.';
  } finally {
    loadingRecommendations.value = false;
  }
}

async function checkAvailability() {
  error.value = '';
  success.value = '';
  previewError.value = '';

  const payload = buildPayload();
  if (!payload) return;

  checkingPreview.value = true;

  try {
    preview.value = props.currentCustomer
      ? await api.previewMyOrder(props.currentCustomer.id, payload)
      : await api.previewOrder(payload);
  } catch (err) {
    previewError.value = err instanceof Error ? err.message : 'Could not preview this order.';
  } finally {
    checkingPreview.value = false;
  }
}

async function submitOrder() {
  error.value = '';
  success.value = '';
  previewError.value = '';

  const payload = buildPayload();
  if (!payload) return;

  submitting.value = true;

  try {
    if (props.currentCustomer) {
      await api.createMyOrder(props.currentCustomer.id, payload);
    } else {
      await api.createOrder(payload);
    }

    success.value = 'Order accepted. Stock has been reserved for the delivery date.';
    items.value = [];
    clearPreview();
    emit('created');
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Order could not be created.';
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <section class="card order-form-card">
    <div class="card-header">
      <div>
        <p class="eyebrow">Create order</p>
        <h2>{{ currentCustomer ? 'Submit your order' : 'Submit a customer order' }}</h2>
      </div>
      <span class="badge">{{ items.length }} items</span>
    </div>

    <div v-if="currentCustomer" class="customer-context">
      <div>
        <p class="eyebrow">Logged in customer</p>
        <strong>{{ currentCustomer.name }}</strong>
        <small>
          {{ currentCustomer.addressLine1 }}, {{ currentCustomer.city }} · {{ currentCustomer.postcode }}
        </small>
      </div>
      <span class="badge">Private order portal</span>
    </div>

    <div class="assistant-panel">
      <div>
        <p class="eyebrow">Smart Order Assistant</p>
        <h3>Describe what you are trying to order</h3>
        <p>
          Recommendations only come from the valid product catalogue. When logged in, your previous
          orders improve the ranking.
        </p>
      </div>

      <div class="assistant-search">
        <input
          v-model="assistantQuery"
          placeholder="e.g. breakfast for a cafe, bakery restock, drinks for lunch"
        />
        <button
          type="button"
          class="secondary"
          :disabled="loadingRecommendations"
          @click="findRecommendations"
        >
          {{ loadingRecommendations ? 'Finding...' : 'Find suggestions' }}
        </button>
      </div>

      <p v-if="recommendationError" class="error">{{ recommendationError }}</p>

      <div v-if="recommendations.length" class="recommendation-grid">
        <article
          v-for="recommendation in recommendations"
          :key="recommendation.sku"
          class="recommendation-card"
        >
          <strong>{{ recommendation.name }}</strong>
          <small>{{ recommendation.reason }}</small>

          <span v-if="recommendation.suggestedUnitPrice" class="price-chip">
            estimated unit £{{ recommendation.suggestedUnitPrice.toFixed(2) }}
          </span>

          <div class="recommendation-actions">
            <button
              type="button"
              class="secondary compact"
              @click="useRecommendation(recommendation)"
            >
              Use
            </button>
            <button
              type="button"
              class="compact"
              @click="addRecommendation(recommendation)"
            >
              Add
            </button>
          </div>
        </article>
      </div>
    </div>

    <div class="form-grid" :class="{ locked: currentCustomer }">
      <label v-if="!currentCustomer">
        Existing customer
        <select v-model="form.selectedCustomerId">
          <option value="">New customer / manual details</option>
          <option
            v-for="customer in customers"
            :key="customer.id"
            :value="customer.id"
          >
            {{ customer.name }} · {{ customer.postcode }}
          </option>
        </select>
      </label>

      <label>
        Customer name
        <input
          v-model="form.customerName"
          :readonly="!!currentCustomer"
          placeholder="Northside Kitchen"
        />
      </label>

      <label>
        Address line 1
        <input
          v-model="form.addressLine1"
          :readonly="!!currentCustomer"
          placeholder="12 Granary Square"
        />
      </label>

      <label>
        Postcode
        <input
          v-model="form.postcode"
          :readonly="!!currentCustomer"
          placeholder="N1C 4AG"
        />
      </label>

      <label>
        City
        <input
          v-model="form.city"
          :readonly="!!currentCustomer"
          placeholder="London"
        />
      </label>

      <label>
        Delivery date
        <input v-model="form.deliveryDate" type="date" />
      </label>
    </div>

    <div class="item-builder">
      <label>
        Product
        <select v-model="form.selectedSku">
          <option value="">Select product</option>
          <option
            v-for="product in products"
            :key="product.sku"
            :value="product.sku"
          >
            {{ product.name }} · {{ product.sku }}
          </option>
        </select>
      </label>

      <label>
        Quantity
        <input v-model.number="form.quantity" type="number" min="1" step="1" />
      </label>

      <label>
        Item total (£)
        <input v-model.number="form.itemTotal" type="number" min="0.01" step="0.01" />
      </label>

      <button type="button" class="secondary" @click="addItem">
        Add item
      </button>
    </div>

    <p v-if="selectedProduct" class="hint">Selected: {{ selectedProduct.name }}</p>

    <div v-if="items.length" class="basket">
      <div
        v-for="(item, index) in items"
        :key="`${item.productSku}-${index}`"
        class="basket-row"
      >
        <span>{{ productName(item.productSku) }} × {{ item.quantity }}</span>
        <strong>£{{ item.itemTotal.toFixed(2) }}</strong>
        <button type="button" class="ghost" @click="removeItem(index)">
          Remove
        </button>
      </div>

      <div class="basket-total">
        <span>Total</span>
        <strong>£{{ total.toFixed(2) }}</strong>
      </div>
    </div>

    <section
      v-if="preview"
      class="preview-panel"
      :class="{ ready: preview.canSubmit, blocked: !preview.canSubmit }"
    >
      <div class="preview-header">
        <div>
          <p class="eyebrow">Availability preview</p>
          <h3>{{ preview.canSubmit ? 'Ready to submit' : 'Cannot fulfil yet' }}</h3>
        </div>

        <span class="badge" :class="{ warning: !preview.canSubmit }">
          {{ preview.canSubmit ? 'Available' : 'Blocked' }}
        </span>
      </div>

      <p v-if="preview.deliveryDate" class="hint">
        Delivery date: {{ preview.deliveryDate }} · Basket total:
        £{{ preview.total.toFixed(2) }}
      </p>

      <div v-if="preview.plannedReservations.length" class="planned-reservations">
        <strong>Planned stock allocation</strong>

        <div
          v-for="reservation in preview.plannedReservations"
          :key="`${reservation.stockBatchId}-${reservation.productSku}`"
          class="reservation-row"
        >
          <span>{{ reservation.productName }}</span>
          <small>
            {{ reservation.quantity }} units from {{ reservation.stockBatchId }}
            · expires {{ reservation.expiryDate }}
          </small>
        </div>
      </div>

      <ul v-if="preview.reasons.length" class="preview-list">
        <li v-for="reason in preview.reasons" :key="reason">
          {{ reason }}
        </li>
      </ul>

      <ul v-if="preview.warnings.length" class="preview-list muted">
        <li v-for="warning in preview.warnings" :key="warning">
          {{ warning }}
        </li>
      </ul>
    </section>

    <p v-if="previewError" class="error">{{ previewError }}</p>
    <p v-if="error" class="error">{{ error }}</p>
    <p v-if="success" class="success">{{ success }}</p>

    <div class="submit-actions">
      <button
        type="button"
        class="secondary"
        :disabled="checkingPreview"
        @click="checkAvailability"
      >
        {{ checkingPreview ? 'Checking...' : 'Check availability' }}
      </button>

      <button :disabled="submitting" @click="submitOrder">
        {{ submitting ? 'Submitting...' : 'Submit order' }}
      </button>
    </div>
  </section>
</template>