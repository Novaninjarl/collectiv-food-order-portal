<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { api, type Customer, type EventLog, type Order, type Product, type RejectedOrder } from './api';
import OrderForm from './components/OrderForm.vue';
import OrdersTable from './components/OrdersTable.vue';
import RejectedOrders from './components/RejectedOrders.vue';
import CustomersPanel from './components/CustomersPanel.vue';
import EventsPanel from './components/EventsPanel.vue';

const SESSION_KEY = 'collectiv-current-customer-id';

type ViewMode = 'public' | 'customer' | 'admin';

const products = ref<Product[]>([]);
const customers = ref<Customer[]>([]);
const customerOrders = ref<Order[]>([]);
const customerRejectedOrders = ref<RejectedOrder[]>([]);
const customerEvents = ref<EventLog[]>([]);
const adminOrders = ref<Order[]>([]);
const adminRejectedOrders = ref<RejectedOrder[]>([]);
const adminEvents = ref<EventLog[]>([]);
const loading = ref(true);
const error = ref('');
const viewMode = ref<ViewMode>('public');
const selectedLoginCustomerId = ref('');
const currentCustomerId = ref<number | null>(null);

const currentCustomer = computed(() => {
  if (!currentCustomerId.value) return null;
  return customers.value.find(c => c.id === currentCustomerId.value) ?? null;
});

const publicPopularProducts = computed(() => products.value.slice(0, 8));

async function loadBase() {
  const [productData, customerData] = await Promise.all([
    api.products(),
    api.customers()
  ]);

  products.value = productData;
  customers.value = customerData;
}

async function loadCustomerData() {
  if (!currentCustomerId.value) return;

  const [orders, rejected, events, refreshedCustomer] = await Promise.all([
    api.myOrders(currentCustomerId.value),
    api.myRejectedOrders(currentCustomerId.value),
    api.myEvents(currentCustomerId.value),
    api.me(currentCustomerId.value)
  ]);

  customerOrders.value = orders;
  customerRejectedOrders.value = rejected;
  customerEvents.value = events;

  customers.value = customers.value.map(customer =>
    customer.id === refreshedCustomer.id ? refreshedCustomer : customer
  );
}

async function loadAdminData() {
  const [orders, rejected, events, customerData] = await Promise.all([
    api.orders(),
    api.rejectedOrders(),
    api.events(),
    api.customers()
  ]);

  adminOrders.value = orders;
  adminRejectedOrders.value = rejected;
  adminEvents.value = events;
  customers.value = customerData;
}

async function refresh() {
  error.value = '';
  loading.value = true;

  try {
    await loadBase();

    if (viewMode.value === 'customer' && currentCustomerId.value) {
      await loadCustomerData();
    }

    if (viewMode.value === 'admin') {
      await loadAdminData();
    }
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Could not load app data. Is the .NET API running?';
  } finally {
    loading.value = false;
  }
}

async function login() {
  if (!selectedLoginCustomerId.value) {
    error.value = 'Choose a customer to continue.';
    return;
  }

  currentCustomerId.value = Number(selectedLoginCustomerId.value);
  localStorage.setItem(SESSION_KEY, selectedLoginCustomerId.value);
  viewMode.value = 'customer';
  await refresh();
}

async function logout() {
  currentCustomerId.value = null;
  selectedLoginCustomerId.value = '';
  customerOrders.value = [];
  customerRejectedOrders.value = [];
  customerEvents.value = [];
  localStorage.removeItem(SESSION_KEY);
  viewMode.value = 'public';
  await refresh();
}

async function openAdminDemo() {
  viewMode.value = 'admin';
  await refresh();
}

async function backToCustomerPortal() {
  if (currentCustomerId.value) {
    viewMode.value = 'customer';
    await refresh();
  } else {
    viewMode.value = 'public';
    await refresh();
  }
}

onMounted(async () => {
  const savedCustomerId = localStorage.getItem(SESSION_KEY);
  if (savedCustomerId) {
    currentCustomerId.value = Number(savedCustomerId);
    selectedLoginCustomerId.value = savedCustomerId;
    viewMode.value = 'customer';
  }

  await refresh();
});
</script>

<template>
  <main class="app-shell">
    <nav class="top-nav">
      <div class="brand-mark">
        <span class="brand-dot"></span>
        <strong>Collectiv</strong>
      </div>

      <div class="nav-links">
        <button class="nav-link" @click="viewMode = 'public'">Products</button>
        <button v-if="currentCustomer" class="nav-link" @click="backToCustomerPortal">My portal</button>
        <button class="nav-link" @click="openAdminDemo">Admin demo</button>
      </div>

      <div class="nav-session">
        <span v-if="currentCustomer">Signed in as {{ currentCustomer.name }}</span>
        <span v-else>Customer portal</span>
        <button v-if="currentCustomer" class="secondary compact" @click="logout">Log out</button>
      </div>
    </nav>

    <p v-if="error" class="error floating-message">{{ error }}</p>
    <p v-if="loading" class="loading floating-message">Loading order platform...</p>

    <template v-else>
      <section v-if="viewMode === 'public'" class="public-shell">
        <section class="landing-hero">
          <div class="hero-copy">
            <p class="eyebrow">Client-facing order management</p>
            <h1>Order smarter</h1>
            <p>
              A private customer portal for professional kitchens to discover catalogue products,
              submit validated orders, repeat previous baskets and receive smart suggestions from
              their own order history.
            </p>

            <div class="hero-actions">
              <button @click="login">Enter selected portal</button>
              <button class="secondary" @click="openAdminDemo">Review admin demo</button>
            </div>
          </div>

          <section class="glass-card login-card hero-login">
            <p class="eyebrow">Mock customer login</p>
            <h2>Choose your account</h2>
            <p>
              Logged-in customers only see their own orders. Public users can browse the catalogue,
              but cannot view another customer’s order history.
            </p>

            <label>
              Customer
              <select v-model="selectedLoginCustomerId">
                <option value="">Select a customer</option>
                <option v-for="customer in customers" :key="customer.id" :value="customer.id">
                  {{ customer.name }} · {{ customer.city }} · {{ customer.postcode }}
                </option>
              </select>
            </label>

            <button @click="login">Continue to my orders</button>
          </section>

          <div class="hero-watermark">Orders</div>

          <aside class="floating-product-card">
            <p class="eyebrow">Smart basket preview</p>
            <h3>Cafe breakfast</h3>
            <div class="chip-row">
              <span>Eggs</span>
              <span>Milk</span>
              <span>Sourdough</span>
              <span>Juice</span>
            </div>
          </aside>
        </section>

        <section class="glass-card catalogue-panel">
          <div class="card-header">
            <div>
              <p class="eyebrow">Public catalogue</p>
              <h2>Products available to order</h2>
            </div>
            <span class="badge">{{ products.length }} products</span>
          </div>

          <p class="hint">
            Recommendations and orders are limited to this catalogue. The assistant helps customers
            find products, but it never invents products outside the valid product list.
          </p>

          <div class="catalogue-grid product-showcase">
            <article v-for="product in publicPopularProducts" :key="product.sku" class="catalogue-item">
              <span class="product-orb"></span>
              <strong>{{ product.name }}</strong>
              <small>{{ product.sku }}</small>
            </article>
          </div>
        </section>

        <div class="stats-grid">
          <article class="stat-card">
            <span>Catalogue products</span>
            <strong>{{ products.length }}</strong>
          </article>
          <article class="stat-card">
            <span>Customer accounts</span>
            <strong>{{ customers.length }}</strong>
          </article>
          <article class="stat-card">
            <span>Order history</span>
            <strong>Private</strong>
          </article>
          <article class="stat-card">
            <span>Review mode</span>
            <strong>Admin</strong>
          </article>
        </div>
      </section>

      <section v-if="viewMode === 'customer' && currentCustomer" class="customer-shell">
        <section class="customer-banner">
          <div>
            <p class="eyebrow">Private portal</p>
            <h2>{{ currentCustomer.name }}</h2>
            <p>{{ currentCustomer.addressLine1 }}, {{ currentCustomer.city }} · {{ currentCustomer.postcode }}</p>
          </div>

          <div class="customer-banner-stats">
            <span><strong>{{ customerOrders.length }}</strong> accepted</span>
            <span><strong>{{ customerRejectedOrders.length }}</strong> rejected</span>
            <span><strong>{{ customerEvents.length }}</strong> events</span>
          </div>
        </section>

        <div class="dashboard-grid">
          <OrderForm
            :products="products"
            :customers="customers"
            :current-customer="currentCustomer"
            @created="refresh"
          />

          <section class="side-stack">
            <section class="glass-card insight-card">
              <p class="eyebrow">Customer context</p>
              <h2>Personalised ordering</h2>
              <p>
                The Smart Order Assistant ranks suggestions using the product catalogue,
                simple scenario semantics, product popularity and this customer’s previous orders.
              </p>

              <div class="mini-metric-grid">
                <div>
                  <strong>{{ customerOrders.length }}</strong>
                  <span>accepted orders</span>
                </div>
                <div>
                  <strong>{{ products.length }}</strong>
                  <span>catalogue items</span>
                </div>
              </div>
            </section>

            <RejectedOrders
              :rejected-orders="customerRejectedOrders"
              eyebrow="My rejected orders"
              title="Failed submissions"
            />
          </section>
        </div>

        <OrdersTable
          :orders="customerOrders"
          :current-customer-id="currentCustomer.id"
          title="My order history"
          eyebrow="Private customer history"
          :show-customer="false"
          @changed="refresh"
        />

        <EventsPanel :events="customerEvents" />
      </section>

      <section v-if="viewMode === 'admin'" class="admin-shell">
        <section class="admin-note glass-card">
          <p class="eyebrow">Admin/demo view</p>
          <h2>All imported platform data</h2>
          <p>
            This view exists so reviewers can verify the full dataset, rejected imports and event log.
            It is intentionally separated from the customer portal because customer order history
            should not be public.
          </p>
          <button class="secondary compact" @click="backToCustomerPortal">Back to customer portal</button>
        </section>

        <div class="stats-grid">
          <article class="stat-card">
            <span>Accepted orders</span>
            <strong>{{ adminOrders.length }}</strong>
          </article>
          <article class="stat-card">
            <span>Rejected orders</span>
            <strong>{{ adminRejectedOrders.length }}</strong>
          </article>
          <article class="stat-card">
            <span>Customers</span>
            <strong>{{ customers.length }}</strong>
          </article>
          <article class="stat-card">
            <span>Products</span>
            <strong>{{ products.length }}</strong>
          </article>
        </div>

        <OrdersTable :orders="adminOrders" @changed="refresh" />

        <div class="layout">
          <RejectedOrders
            :rejected-orders="adminRejectedOrders"
            eyebrow="Rejected imports"
            title="Clear validation failures"
          />
          <CustomersPanel :customers="customers" />
        </div>

        <EventsPanel :events="adminEvents" />
      </section>
    </template>
  </main>
</template>